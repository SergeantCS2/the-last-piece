# Syncplay server for movie night.
# Syncplay isn't published on PyPI, so we pull the tagged source release
# and install only the server's requirements (no Qt GUI deps).
FROM python:3.13-slim

ARG SYNCPLAY_VERSION=1.7.6

ADD https://github.com/Syncplay/syncplay/archive/refs/tags/v${SYNCPLAY_VERSION}.tar.gz /tmp/syncplay.tar.gz

RUN mkdir -p /opt/syncplay \
 && tar -xzf /tmp/syncplay.tar.gz -C /opt/syncplay --strip-components=1 \
 && rm /tmp/syncplay.tar.gz \
 && pip install --no-cache-dir -r /opt/syncplay/requirements.txt \
 && useradd --system --uid 10001 --home-dir /data syncplay \
 && mkdir -p /data \
 && chown syncplay /data

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

USER syncplay
WORKDIR /data
EXPOSE 8999

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
