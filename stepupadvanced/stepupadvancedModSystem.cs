using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace stepupadvanced;

public class stepupadvancedModSystem : ModSystem
{
    private bool stepUpEnabled = true;
    private ICoreClientAPI capi;
    private const float MinStepHeight = 0.5f;
    private const float AbsoluteMaxStepHeight = 3.0f;

    private IServerNetworkChannel serverChannel;
    private IClientNetworkChannel clientChannel;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        stepupadvancedConfig.Load(api);
        api.World.Logger.Event("Initialized 'StepUp Advanced' mod");
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        stepupadvancedServerConfig.Load(sapi);

        serverChannel = sapi.Network.RegisterChannel("stepupadvanced")
            .RegisterMessageType<stepupadvancedServerConfig>()
            .SetMessageHandler<stepupadvancedServerConfig>((player, config) =>
            {
                sapi.World.Logger.Event("Received StepUp config from server: {0}", config);
            });

        sapi.Event.PlayerNowPlaying += player =>
        {
            var serverConfig = stepupadvancedServerConfig.Current;
            serverChannel.SendPacket(serverConfig, player);
        };
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        clientChannel = capi.Network.RegisterChannel("stepupadvanced")
            .RegisterMessageType<stepupadvancedServerConfig>()
            .SetMessageHandler<stepupadvancedServerConfig>(config =>
            {
                ApplyServerConfig(config);
                capi.ShowChatMessage("Server config applied.");
            });

        capi.Event.RegisterGameTickListener(ValidateClientConfig, 5000);

        api.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        api.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
    }

    private void ValidateClientConfig(float dt)
    {
        var clientConfig = stepupadvancedConfig.Current;
        var serverConfig = stepupadvancedServerConfig.Current;

        if (!serverConfig.AllowStepUpAdvanced)
        {
            stepUpEnabled = false;
            capi.ShowChatMessage("StepUp Advanced is disabled by the server.");
        }

        clientConfig.StepHeight = Math.Min(clientConfig.StepHeight, serverConfig.MaxStepHeight);
    }

    private void ApplyServerConfig(stepupadvancedServerConfig serverConfig)
    {
        if (!serverConfig.AllowStepUpAdvanced)
        {
            stepUpEnabled = false;
            capi.ShowChatMessage("StepUp Advanced is disabled by the server.");
        }
        else
        {
            stepupadvancedConfig.Current.StepHeight = Math.Min(stepupadvancedConfig.Current.StepHeight, serverConfig.MaxStepHeight);
            ApplyStepHeightToPlayer();
        }
    }

    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        var config = stepupadvancedConfig.Current;
        if (config.StepHeight < AbsoluteMaxStepHeight)
        {
            config.StepHeight += config.StepHeightIncrement;
            config.StepHeight = Math.Min(config.StepHeight, AbsoluteMaxStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height increased to {config.StepHeight:0.0} blocks.");
            stepupadvancedConfig.Save(capi);
            return true;
        }

        capi.ShowChatMessage($"Step height cannot exceed {AbsoluteMaxStepHeight} blocks.");
        return false;
    }

    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        var config = stepupadvancedConfig.Current;
        if (config.StepHeight > MinStepHeight)
        {
            config.StepHeight -= config.StepHeightIncrement;
            config.StepHeight = Math.Max(config.StepHeight, MinStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height decreased to {config.StepHeight:0.0} blocks.");
            stepupadvancedConfig.Save(capi);
            return true;
        }

        capi.ShowChatMessage($"Step height cannot be less than {MinStepHeight} blocks.");
        return false;
    }

    private void ApplyStepHeightToPlayer()
    {
        var player = capi.World.Player;
        player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepupadvancedConfig.Current.StepHeight;
    }
}