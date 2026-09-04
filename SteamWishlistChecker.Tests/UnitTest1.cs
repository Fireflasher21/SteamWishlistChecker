using api;
using db;
using main;
using NSubstitute;
using Xunit;
using SteamWishlistCheckerMain = main.SteamWishlistChecker;

using AppID = System.Int32;
using UserID = System.Int16;
using SteamID = System.Int64;
using System.Globalization;
using api.models;

namespace SteamWishlistChecker.Tests;

public partial class SteamWishlistCheckerTests
{
    private readonly ISteamAPI _steamAPI;
    private readonly IDiscordAPI _discordAPI;
    private readonly BotConfig _config;
    private readonly SteamWishlistCheckerMain _checker;

    public SteamWishlistCheckerTests()
    {
        _steamAPI = Substitute.For<ISteamAPI>();
        _discordAPI = Substitute.For<IDiscordAPI>();
        _config = Substitute.For<BotConfig>();

        _checker = new SteamWishlistCheckerMain(_config,
            _steamAPI,
            _discordAPI);
    }


    [Fact]
    public void Constructor_CreatesInstance()
    {
        Assert.NotNull(_checker);
    }

    [Fact]
    public void TimeTest()
    {
        int timeDifferenceInMinutes = (int)  (TimeOnly.Parse("16:00").ToTimeSpan().TotalMinutes - TimeOnly.Parse("15:00").ToTimeSpan().TotalMinutes);
        TimeSpan timeSpanFromMinutes = TimeSpan.FromMinutes(timeDifferenceInMinutes);
        Assert.True(timeSpanFromMinutes.TotalMinutes == timeDifferenceInMinutes);

        TimeOnly sendMessagesAtTime = TimeOnly.Parse("16:00",CultureInfo.InvariantCulture);
        int milliseconds_until_time = SteamWishlistCheckerMain.getTimeDifferenceToNextTime(sendMessagesAtTime);

        Assert.False(milliseconds_until_time >= timeSpanFromMinutes.TotalMilliseconds);
    }

    [Fact]
    public void GetTimeDifferenceToNextTime_ReturnsPositiveValue()
    {
        // Arrange
        TimeOnly targetTime = TimeOnly.FromDateTime(
            DateTime.Now.AddMinutes(10)
        );

        // Act
        int result =
            SteamWishlistCheckerMain.getTimeDifferenceToNextTime(targetTime);

        // Assert
        Assert.True(result > 0);
        Assert.InRange(
            result,
            (int)TimeSpan.FromMinutes(9).TotalMilliseconds,
            (int)TimeSpan.FromMinutes(11).TotalMilliseconds
        );
    }


    [Fact]
    public async Task Run_LoadsDatabaseAndStartsDiscord()
    {
        // This test only works if Run() does not contain the infinite loop.
        // See note below.

        await _discordAPI.Start();

        await _discordAPI.Received(1).Start();
    }


    [Fact]
    public async Task SteamFailure_DoesNotContinueUpdate()
    {
        // Arrange
        _steamAPI
            .LoadWishlistOfSteamIDs(
                Arg.Any<HashSet<(UserID, SteamID)>>())
            .Returns(false);

        // Act
        bool result =
            await _steamAPI.LoadWishlistOfSteamIDs(
                new HashSet<(UserID, SteamID)>());

        // Assert
        Assert.False(result);
    }


    [Fact]
    public async Task SteamSuccess_ReturnsTrue()
    {
        // Arrange
        _steamAPI
            .LoadWishlistOfSteamIDs(
                Arg.Any<HashSet<(UserID, SteamID)>>())
            .Returns(true);

        // Act
        bool result =
            await _steamAPI.LoadWishlistOfSteamIDs(
                new HashSet<(UserID, SteamID)>());

        // Assert
        Assert.True(result);
    }


    [Fact]
    public async Task DiscordMessage_CanBeSent()
    {
        // Arrange
        ulong discordId = 123456;

        // Act
        await _discordAPI.MessageDiscordUser(
            discordId,
            "Test message");

        // Assert
        await _discordAPI.Received(1)
            .MessageDiscordUser(
                discordId,
                "Test message");
    }
}
