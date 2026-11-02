using Tlp.Core;
using Tlp.Host.Services;

namespace Tlp.Tests;

public class NightPageTests
{
    private static Pack Sample(string? password) => new()
    {
        Title = "Friday <night>",
        Syncplay = new SyncplayInfo { Host = "sync.example.com", Port = 8999, Room = "movienight", Password = password },
        Items = [new PackItem { FileName = "Show - 01.mkv", Sources = [new PackSource { Kind = PackSourceKind.Http, Uri = "https://h.example/n/t/files/0/Show%20-%2001.mkv" }] }],
    };

    [Fact]
    public void Phone_section_lists_what_synkplay_needs_with_copy_buttons()
    {
        var html = NightPage.Render(Sample("p\"w<1"), "https://h.example/n/t/pack.json");
        Assert.Contains("On a phone", html);
        Assert.Contains("data-copy=\"sync.example.com\"", html);
        Assert.Contains("data-copy=\"8999\"", html);
        Assert.Contains("data-copy=\"movienight\"", html);
        // Encoded, so a password can't break out of the attribute.
        Assert.Contains("data-copy=\"p&quot;w&lt;1\"", html);
        Assert.Contains("Friday &lt;night&gt;", html);
    }

    [Fact]
    public void No_password_row_without_a_password()
    {
        var html = NightPage.Render(Sample(null), "https://h.example/n/t/pack.json");
        Assert.DoesNotContain("<span>Password</span>", html);
    }
}

public class GroupNameTests
{
    [Fact]
    public void Group_name_survives_the_trimmed_pack_json_and_old_packs_have_none()
    {
        var pack = new Pack
        {
            Title = "Friday",
            Group = "Friday crew",
            Syncplay = new SyncplayInfo { Host = "h", Room = "r" },
            Items = [],
        };
        var json = PackJson.SerializePack(pack);
        Assert.Contains("\"group\": \"Friday crew\"", json);
        Assert.Equal("Friday crew", PackJson.DeserializePack(json)!.Group);
        Assert.Null(PackJson.DeserializePack("""{"title":"x","syncplay":{"host":"h","room":"r"},"items":[]}""")!.Group);
    }
}
