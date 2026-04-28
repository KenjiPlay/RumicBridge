using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace RumicBridge;

[ApiVersion(2, 1)]
public class RumicBridgePlugin : TerrariaPlugin
{
    private const string PluginFolderName = "RumicBridge";
    private const string RewardsFileName = "pending_rewards.json";

    private DateTime _lastCheck = DateTime.MinValue;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);

    public override string Name => "RumicBridge";
    public override string Author => "Rumic";
    public override string Description => "Bridge para entregar recompensas desde Discord a Terraria.";
    public override Version Version => new Version(1, 0, 0);

    private string PluginFolder => Path.Combine(TShock.SavePath, PluginFolderName);
    private string RewardsPath => Path.Combine(PluginFolder, RewardsFileName);

    public RumicBridgePlugin(Main game) : base(game)
    {
    }

    public override void Initialize()
    {
        Directory.CreateDirectory(PluginFolder);

        if (!File.Exists(RewardsPath))
        {
            File.WriteAllText(RewardsPath, "[]");
        }

        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        TShock.Log.ConsoleInfo("[RumicBridge] Plugin cargado correctamente.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
        }

        base.Dispose(disposing);
    }

    private void OnGameUpdate(EventArgs args)
    {
        if (DateTime.UtcNow - _lastCheck < _checkInterval)
        {
            return;
        }

        _lastCheck = DateTime.UtcNow;
        ProcessPendingRewards();
    }

    private void ProcessPendingRewards()
    {
        List<RumicReward> rewards;

        try
        {
            var json = File.ReadAllText(RewardsPath);
            rewards = JsonSerializer.Deserialize<List<RumicReward>>(json) ?? new List<RumicReward>();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[RumicBridge] No se pudo leer pending_rewards.json: {ex.Message}");
            return;
        }

        var changed = false;

        foreach (var reward in rewards)
        {
            if (reward.Delivered)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(reward.Player) || string.IsNullOrWhiteSpace(reward.Item))
            {
                reward.Delivered = true;
                reward.Error = "Datos incompletos.";
                changed = true;
                continue;
            }

            var player = FindPlayerByName(reward.Player);

            if (player == null)
            {
                continue;
            }

            var amount = Math.Max(1, reward.Amount);
            var itemId = TShock.Utils.GetItemByIdOrName(reward.Item);

            if (itemId.Count == 0)
            {
                reward.Delivered = true;
                reward.Error = $"Item no encontrado: {reward.Item}";
                changed = true;
                continue;
            }

            if (itemId.Count > 1)
            {
                reward.Delivered = true;
                reward.Error = $"Item ambiguo: {reward.Item}";
                changed = true;
                continue;
            }

            var item = itemId[0];

            player.GiveItem(item.type, item.Name, item.width, item.height, amount);

            reward.Delivered = true;
            reward.DeliveredAt = DateTime.UtcNow.ToString("O");
            changed = true;

            player.SendSuccessMessage($"[Rumic] Recibiste {amount}x {item.Name}.");
            TShock.Log.ConsoleInfo($"[RumicBridge] Entregado {amount}x {item.Name} a {player.Name}.");
        }

        if (!changed)
        {
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(RewardsPath, JsonSerializer.Serialize(rewards, options));
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[RumicBridge] No se pudo guardar pending_rewards.json: {ex.Message}");
        }
    }

    private static TSPlayer? FindPlayerByName(string name)
    {
        foreach (var player in TShock.Players)
        {
            if (player == null || !player.Active)
            {
                continue;
            }

            if (string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return player;
            }
        }

        return null;
    }
}

public class RumicReward
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Player { get; set; } = "";
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public bool Delivered { get; set; } = false;
    public string? DeliveredAt { get; set; }
    public string? Error { get; set; }
}