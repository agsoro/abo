using Abo.Integrations.XpectoLive;
using Abo.Integrations.XpectoLive.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Abo.Tests;

public class XpectoLiveWikiClientIntegrationTests
{
    private readonly IXpectoLiveWikiClient _client;
    
    public XpectoLiveWikiClientIntegrationTests()
    {
        // Setup configuration to read from appsettings.json
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.GetFullPath(@"..\..\..\..\Abo.Pm"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets("2382c563-3cdf-48a5-a819-7cc76d5a465c")
            .AddEnvironmentVariables()
            .Build();

        var options = new XpectoLiveOptions();
        config.GetSection("Integrations:XpectoLive").Bind(options);

        // Sanity check to avoid 401s if credentials aren't found
        Assert.False(string.IsNullOrEmpty(options.ApiKey), "Valid API credentials were not found in AppSettings or UserSecrets.");

        // Ideally an integration test should assert the API Key is present,
        // otherwise we skip the test or it will fail with 401.
        
        var optionsMonitor = Options.Create(options);
        var httpClient = new HttpClient();
        var logger = NullLogger<XpectoLiveWikiClient>.Instance;
        
        _client = new XpectoLiveWikiClient(httpClient, optionsMonitor, logger);
    }

    [Fact]
    public async Task UpdateAboSpaceFirstPage_IntegrationTest()
    {
        // 1. Check if the 'abo' space exists
        var spaces = await _client.GetSpacesAsync();
        var aboSpace = spaces.FirstOrDefault(s => s.Title != null && s.Title.Equals("abo", StringComparison.OrdinalIgnoreCase));

        // 2. Create if necessary
        if (aboSpace == null)
        {
            aboSpace = await _client.CreateSpaceAsync(new SpaceNew { Id = "abo", Title = "abo" });
        }

        Assert.NotNull(aboSpace);
        
        // Wait, GetSpacesAsync might not return the full space object with StartPage, fetch Space directly to get tree
        if (aboSpace.Id != null) 
        {
            aboSpace = await _client.GetSpaceAsync(aboSpace.Id);
        }

        Assert.NotNull(aboSpace.StartPage);
        Assert.NotNull(aboSpace.StartPage.Id);

        var firstPageId = aboSpace.StartPage.Id!;
        var spaceId = aboSpace.Id!;

        // 3. Update the comment of the first page (as a draft)
        var newComment = $"Integration test updated via Abo App at {DateTime.Now:O}";
        var update = new ContentUpdate
        {
            VersionComment = newComment
            // We can also set 'Content' here if we wanted to change the page content.
        };

        var draftPage = await _client.UpdatePageDraftAsync(spaceId, firstPageId, update);
        Assert.NotNull(draftPage);
        Assert.Equal(newComment, draftPage.VersionComment);

        // 4. Publish the edited page afterwards
        var publishedPage = await _client.PublishPageDraftAsync(spaceId, firstPageId);
        Assert.NotNull(publishedPage);
    }
}
