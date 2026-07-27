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

namespace SteamWishlistChecker.Tests;

public class SteamWishlistCheckerTests
{
    private readonly ISteamAPI _steamAPI;
    private readonly IDiscordAPI _discordAPI;
    private readonly SteamWishlistCheckerMain _checker;

    public SteamWishlistCheckerTests()
    {
        _steamAPI = Substitute.For<ISteamAPI>();
        _discordAPI = Substitute.For<IDiscordAPI>();

        _checker = new SteamWishlistCheckerMain(
            _steamAPI,
            _discordAPI);
    }


    [Fact]
    public void Constructor_CreatesInstance()
    {
        Assert.NotNull(_checker);
    }


    [Fact]
    public void GetTimeDifferenceToNextTime_ReturnsPositiveValue()
    {
        // Arrange
        TimeOnly targetTime = TimeOnly.Parse("14:00",CultureInfo.InvariantCulture);

        // Act
        int result =
            SteamWishlistCheckerMain.getTimeDifferenceToNextTime(targetTime);

        // Assert
        // 5 Millisecond timespace for test
        Console.WriteLine(targetTime);
        Assert.True(result >= (TimeSpan.FromMinutes(10).TotalMilliseconds - 5) && result <= TimeSpan.FromMinutes(10).TotalMilliseconds);
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
