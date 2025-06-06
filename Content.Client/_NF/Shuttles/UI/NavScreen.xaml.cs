// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.

using Content.Shared._NF.Shuttles.Events;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI
{
    public sealed partial class NavScreen
    {
        private readonly ButtonGroup _buttonGroup = new();
        public event Action<NetEntity?, InertiaDampeningMode>? OnInertiaDampeningModeChanged;
        public event Action<NetEntity?, float>? OnMaxShuttleSpeedChanged;
        public event Action<string, string>? OnNetworkPortButtonPressed;

        private void NfInitialize()
        {
            // Frontier - IFF search
            IffSearchCriteria.OnTextChanged += args => OnIffSearchChanged(args.Text);

            // Frontier - Maximum IFF Distance
            MaximumIFFDistanceValue.GetChild(0).GetChild(1).Margin = new Thickness(8, 0, 0, 0);
            MaximumIFFDistanceValue.OnValueChanged += args => OnRangeFilterChanged(args);

            // Frontier - Maximum Shuttle Speed
            MaximumShuttleSpeedValue.GetChild(0).GetChild(1).Margin = new Thickness(8, 0, 0, 0);
            MaximumShuttleSpeedValue.OnValueChanged += args => OnMaxSpeedChanged(args);

            DampenerOff.OnPressed += _ => SetDampenerMode(InertiaDampeningMode.Off);
            DampenerOn.OnPressed += _ => SetDampenerMode(InertiaDampeningMode.Dampen);
            AnchorOn.OnPressed += _ => SetDampenerMode(InertiaDampeningMode.Anchor);

            DampenerOff.Group = _buttonGroup;
            DampenerOn.Group = _buttonGroup;
            AnchorOn.Group = _buttonGroup;

            // Network Port Buttons
            DeviceButton1.OnPressed += _ => OnPortButtonPressed("device-button-1", "button-1");
            DeviceButton2.OnPressed += _ => OnPortButtonPressed("device-button-2", "button-2");
            DeviceButton3.OnPressed += _ => OnPortButtonPressed("device-button-3", "button-3");
            DeviceButton4.OnPressed += _ => OnPortButtonPressed("device-button-4", "button-4");
            DeviceButton5.OnPressed += _ => OnPortButtonPressed("device-button-5", "button-5");
            DeviceButton6.OnPressed += _ => OnPortButtonPressed("device-button-6", "button-6");
            DeviceButton7.OnPressed += _ => OnPortButtonPressed("device-button-7", "button-7");
            DeviceButton8.OnPressed += _ => OnPortButtonPressed("device-button-8", "button-8");

            // Send off a request to get the current dampening mode.
            _entManager.TryGetNetEntity(_shuttleEntity, out var shuttle);
            OnInertiaDampeningModeChanged?.Invoke(shuttle, InertiaDampeningMode.Query);
        }

        private void OnPortButtonPressed(string sourcePort, string targetPort)
        {
            OnNetworkPortButtonPressed?.Invoke(sourcePort, targetPort);
        }

        private void SetDampenerMode(InertiaDampeningMode mode)
        {
            NavRadar.DampeningMode = mode;
            _entManager.TryGetNetEntity(_shuttleEntity, out var shuttle);
            OnInertiaDampeningModeChanged?.Invoke(shuttle, mode);
        }

        private void NfUpdateState()
        {
            if (NavRadar.DampeningMode == InertiaDampeningMode.Station)
            {
                DampenerModeButtons.Visible = false;
            }
            else
            {
                DampenerModeButtons.Visible = true;
                DampenerOff.Pressed = NavRadar.DampeningMode == InertiaDampeningMode.Off;
                DampenerOn.Pressed = NavRadar.DampeningMode == InertiaDampeningMode.Dampen;
                AnchorOn.Pressed = NavRadar.DampeningMode == InertiaDampeningMode.Anchor;

                // Disable the Park button (AnchorOn) while in FTL, but keep other dampener buttons enabled
                /*if (NavRadar.InFtl)
                {
                    AnchorOn.Disabled = true;
                    // If the AnchorOn button is pressed while it gets disabled, we need to switch to another mode
                    if (AnchorOn.Pressed)
                    {
                        DampenerOn.Pressed = true;
                        SetDampenerMode(InertiaDampeningMode.Dampen);
                    }
                }
                else
                {
                    AnchorOn.Disabled = false;
                }*/
            }
        }

        // Frontier - Maximum IFF Distance
        private void OnRangeFilterChanged(int value)
        {
            NavRadar.MaximumIFFDistance = (float) value;
        }

        // Frontier - Maximum Shuttle Speed
        private void OnMaxSpeedChanged(int value)
        {
            _entManager.TryGetNetEntity(_shuttleEntity, out var shuttle);
            OnMaxShuttleSpeedChanged?.Invoke(shuttle, value);
        }

        private void NfAddShuttleDesignation(EntityUid? shuttle)
        {
            // Frontier - PR #1284 Add Shuttle Designation
            if (_entManager.TryGetComponent<MetaDataComponent>(shuttle, out var metadata))
            {
                var shipNameParts = metadata.EntityName.Split(' ');
                var designation = shipNameParts[^1];
                if (designation.Length > 2 && designation[2] == '-')
                {
                    NavDisplayLabel.Text = string.Join(' ', shipNameParts[..^1]);
                    ShuttleDesignation.Text = designation;
                }
                else
                    NavDisplayLabel.Text = metadata.EntityName;
            }
            // End Frontier - PR #1284
        }

    }
}
