#!/usr/bin/env python3
"""Smoke test for The Last Piece's Syncplay server.

Speaks Syncplay's line-delimited JSON protocol directly (no Syncplay client
needed) and checks the things a session depends on:

  1. a wrong password is rejected
  2. two viewers can join the same room
  3. when one viewer pauses, the other is told (and by whom)
  4. chat messages reach the other viewer (this is what mpv shows on screen)

Usage:
    python3 tools/smoketest.py --host 127.0.0.1 --port 8999 --password secret

Exits 0 on success, 1 on the first failed check. Standard library only.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import sys

PROTOCOL_VERSION = "1.7.6"  # what we claim to be; the server only uses it for feature gating


class Viewer:
    def __init__(self, name: str, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        self.name = name
        self.reader = reader
        self.writer = writer

    @classmethod
    async def connect(cls, host: str, port: int, name: str) -> "Viewer":
        reader, writer = await asyncio.open_connection(host, port)
        return cls(name, reader, writer)

    async def send(self, message: dict) -> None:
        self.writer.write((json.dumps(message) + "\r\n").encode("utf-8"))
        await self.writer.drain()

    async def hello(self, room: str, password: str | None) -> None:
        hello: dict = {
            "username": self.name,
            "room": {"name": room},
            "version": "1.2.255",
            "realversion": PROTOCOL_VERSION,
            "features": {"chat": True, "readiness": True, "sharedPlaylists": True},
        }
        if password:
            # Syncplay clients send the MD5 hex of the password, not the password.
            hello["password"] = hashlib.md5(password.encode("utf-8")).hexdigest()
        await self.send({"Hello": hello})

    async def wait_for(self, key: str, predicate=lambda body: True, timeout: float = 5.0) -> dict:
        """Read messages until one has `key` and matches `predicate`."""

        async def _loop() -> dict:
            while True:
                line = await self.reader.readline()
                if not line:
                    raise ConnectionError(f"{self.name}: server closed the connection")
                message = json.loads(line.decode("utf-8"))
                if "Error" in message and key != "Error":
                    raise RuntimeError(f"{self.name}: server error: {message['Error']}")
                # Answer server pings so the session looks like a real client.
                if "State" in message and key != "State":
                    await self._ack_state(message["State"])
                    continue
                if key in message and predicate(message[key]):
                    return message[key]

        return await asyncio.wait_for(_loop(), timeout)

    async def _ack_state(self, state: dict) -> None:
        ping = state.get("ping", {})
        await self.send({
            "State": {
                "ping": {"latencyCalculation": ping.get("latencyCalculation", 0), "clientRtt": 0},
            }
        })

    async def close(self) -> None:
        self.writer.close()
        try:
            await self.writer.wait_closed()
        except ConnectionError:
            pass


async def wait_until_ready(host: str, port: int, password: str | None, room: str, seconds: float) -> None:
    """Retry a full Hello round-trip until the server answers.

    An open port isn't enough: Docker's port proxy accepts connections before
    the server inside the container is listening, then drops them.
    """
    loop = asyncio.get_running_loop()
    deadline = loop.time() + seconds
    while True:
        try:
            probe = await Viewer.connect(host, port, "probe")
            await probe.hello(room, password)
            await probe.wait_for("Hello", timeout=2.0)
            await probe.close()
            return
        except (OSError, ConnectionError, asyncio.TimeoutError, json.JSONDecodeError):
            if loop.time() >= deadline:
                raise
            await asyncio.sleep(1.0)


async def run(host: str, port: int, password: str | None, room: str, wait: float) -> None:
    def ok(msg: str) -> None:
        print(f"PASS  {msg}")

    if wait > 0:
        await wait_until_ready(host, port, password, room, wait)
        ok("server is answering")

    # 1. Wrong password is rejected (only meaningful when the server has one).
    if password:
        intruder = await Viewer.connect(host, port, "intruder")
        await intruder.hello(room, password + "-wrong")
        error = await intruder.wait_for("Error")
        assert "password" in error.get("message", "").lower(), error
        await intruder.close()
        ok("wrong password rejected")

    # 2. Two viewers join the same room.
    alice = await Viewer.connect(host, port, "alice")
    await alice.hello(room, password)
    hello = await alice.wait_for("Hello")
    assert hello.get("room", {}).get("name") == room, hello
    ok(f"alice joined room '{room}' (server {hello.get('realversion')})")

    bob = await Viewer.connect(host, port, "bob")
    await bob.hello(room, password)
    await bob.wait_for("Hello")
    ok("bob joined the same room")

    await alice.send({"List": None})
    listing = await alice.wait_for("List", lambda body: room in body and "bob" in body[room])
    assert {"alice", "bob"} <= set(listing[room]), listing
    ok("room lists both viewers")

    # 3. Alice pauses at 42s; Bob should be told, attributed to Alice.
    await alice.send({"State": {"playstate": {"position": 42.0, "paused": True, "doSeek": False}}})
    state = await bob.wait_for(
        "State",
        lambda body: body.get("playstate", {}).get("paused") is True
        and body.get("playstate", {}).get("setBy") == "alice",
    )
    ok(f"bob was paused by alice at {state['playstate']['position']:.1f}s")

    # 4. Chat reaches the other viewer.
    await alice.send({"Chat": "popcorn ready?"})
    chat = await bob.wait_for("Chat", lambda body: body.get("message") == "popcorn ready?")
    assert chat.get("username") == "alice", chat
    ok("chat delivered to bob")

    await alice.close()
    await bob.close()


async def expect_user(host: str, port: int, password: str | None, room: str, user: str, wait: float,
                      need_file: bool = False) -> None:
    """Join as an observer and wait until `user` is in `room` (used to check a real client got in).
    With need_file, also wait until that user's player has a file open."""
    checker = await Viewer.connect(host, port, "checker")
    await checker.hello(room, password)
    await checker.wait_for("Hello")
    loop = asyncio.get_running_loop()
    deadline = loop.time() + wait
    while True:
        await checker.send({"List": None})
        try:
            listing = await checker.wait_for("List", timeout=3.0)
        except asyncio.TimeoutError:
            listing = {}
        users = listing.get(room, {})
        if user in users and (not need_file or (users[user].get("file") or {}).get("name")):
            print(f"PASS  {user} is in room '{room}': {json.dumps(users[user])}")
            await checker.close()
            return
        if loop.time() >= deadline:
            raise AssertionError(f"{user} never joined room '{room}' (saw {sorted(users)})")
        await asyncio.sleep(2.0)


async def expect_playlist(host: str, port: int, password: str | None, room: str, wait: float,
                          files: list[str] | None) -> None:
    """Join `room` and read the shared playlist the server sends on arrival.
    Passes when it isn't empty (or equals `files`, if given). Retries until `wait` runs out.
    Note: anyone watching in that room is paused when this checker leaves (Syncplay's pauseOnLeave)."""
    loop = asyncio.get_running_loop()
    deadline = loop.time() + wait
    while True:
        checker = await Viewer.connect(host, port, "playlist-check")
        await checker.hello(room, password)
        # The server sends the room's playlist just before its Hello reply.
        change = await checker.wait_for("Set", lambda body: "playlistChange" in body)
        got = change["playlistChange"].get("files") or []
        await checker.close()
        if got and (files is None or got == files):
            print(f"PASS  room '{room}' playlist: {got}")
            return
        if loop.time() >= deadline:
            raise AssertionError(f"room '{room}' playlist is {got}, expected {files or 'something'}")
        await asyncio.sleep(3.0)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8999)
    parser.add_argument("--password", default=None)
    parser.add_argument("--room", default="smoketest-temp")
    parser.add_argument("--wait", type=float, default=30.0,
                        help="seconds to wait for the server to answer before testing (0 = don't wait)")
    parser.add_argument("--expect-user", default=None,
                        help="instead of the smoke test, wait until this user is in --room")
    parser.add_argument("--expect-file", action="store_true",
                        help="with --expect-user, also wait until that user's player has a file open")
    parser.add_argument("--expect-playlist", action="store_true",
                        help="instead of the smoke test, wait until --room has a shared playlist")
    parser.add_argument("--playlist-files", default=None,
                        help="with --expect-playlist, the exact files expected, separated by |")
    args = parser.parse_args()

    try:
        if args.expect_playlist:
            files = args.playlist_files.split("|") if args.playlist_files else None
            asyncio.run(expect_playlist(args.host, args.port, args.password, args.room, max(args.wait, 5), files))
        elif args.expect_user:
            asyncio.run(expect_user(args.host, args.port, args.password, args.room, args.expect_user, max(args.wait, 10),
                                    args.expect_file))
        else:
            asyncio.run(run(args.host, args.port, args.password, args.room, args.wait))
    except Exception as exc:  # noqa: BLE001 - report any failure plainly
        print(f"FAIL  {type(exc).__name__}: {exc}")
        return 1
    print("All checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
