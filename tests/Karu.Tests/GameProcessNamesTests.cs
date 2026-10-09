namespace Karu.Tests;

public class GameProcessNamesTests
{
    [Theory]
    [InlineData("VALORANT-Win64-Shipping", true)]
    [InlineData("VALORANT-Win64-Shipping.exe", true)]
    [InlineData("valorant-win64-shipping", true)]
    [InlineData("VALORANT", true)]
    [InlineData("VALORANT.exe", true)]
    [InlineData("RiotClientServices", false)]
    [InlineData("chrome", false)]
    [InlineData("vlc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_valorant_game_process_only(string? name, bool expected)
    {
        Assert.Equal(expected, GameProcessNames.IsValorantProcess(name));
    }
}
