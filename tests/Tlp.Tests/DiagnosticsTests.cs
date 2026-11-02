using System.Net;
using System.Text.Json;
using Tlp.Host.Services;

namespace Tlp.Tests;

public class DiagnosticsTests(HostFixture fx) : IClassFixture<HostFixture>
{
    private static readonly HostEnvironment Plain = new(false, false, "test");
    private static readonly HostEnvironment InWsl = new(true, true, "test");

    [Theory]
    [InlineData("http://localhost:8080", "fail")]
    [InlineData("http://127.0.0.1:8080", "fail")]
    [InlineData("http://192.168.1.20:8080", "warn")]
    [InlineData("http://172.28.144.5:8080", "warn")]
    [InlineData("http://203.0.113.5:8080", "ok")]
    [InlineData("https://movies.example.com", "ok")]
    [InlineData("movies.example.com", "fail")]
    public void Public_link_is_judged_by_who_can_reach_it(string url, string state) =>
        Assert.Equal(state, Diagnostics.PublicLinkCheck(url, Plain).State);

    [Fact]
    public void Wsl_address_gets_wsl_advice()
    {
        var check = Diagnostics.PublicLinkCheck("http://172.28.144.5:8080", InWsl);
        Assert.Equal("warn", check.State);
        Assert.Contains("WSL", check.Fix);
    }

    [Theory]
    [InlineData("10.0.0.1", Diagnostics.AddressKind.Private)]
    [InlineData("172.17.0.1", Diagnostics.AddressKind.Private)]
    [InlineData("172.32.0.1", Diagnostics.AddressKind.Public)]
    [InlineData("100.100.1.1", Diagnostics.AddressKind.Private)]
    [InlineData("::1", Diagnostics.AddressKind.Loopback)]
    [InlineData("fd00::1", Diagnostics.AddressKind.Private)]
    [InlineData("8.8.8.8", Diagnostics.AddressKind.Public)]
    public void Addresses_are_classified(string ip, Diagnostics.AddressKind kind) =>
        Assert.Equal(kind, Diagnostics.Classify(IPAddress.Parse(ip)));

    [Fact]
    public async Task Admin_check_reports_the_test_hosts_problems()
    {
        var res = await fx.Admin().GetAsync("/api/admin/diagnostics");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var checks = doc.RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("area").GetString() + "/" + c.GetProperty("name").GetString(), c => c.GetProperty("state").GetString());
        // The test host's public link is localhost, and nothing runs Syncplay here.
        Assert.Equal("fail", checks["Links/Public link"]);
        Assert.Equal("fail", checks["Syncplay/Server answers"]);
        Assert.Equal("ok", checks["Syncplay/Address friends use"]);
        Assert.Equal("ok", checks["Host/Shelf folder"]);
    }

    [Fact]
    public async Task Diagnostics_need_the_admin_token()
    {
        var res = await fx.Factory.CreateClient().GetAsync("/api/admin/diagnostics");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
