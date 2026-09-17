using Velopack;

// Must run first: handles install, uninstall and update hooks when launched by the installer, and returns
// immediately on a normal start.
VelopackApp.Build().Run();

using var game = new BannerAndBarrow.Game.BannerAndBarrowGame();
game.Run();
