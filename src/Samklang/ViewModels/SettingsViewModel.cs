using System.Collections.ObjectModel;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.SettingsManagement;

namespace Samklang.ViewModels;

/// <summary>
/// The settings page: device targeting, tier mappings, Resting Format, Grace Period, and
/// autostart (issue #9's settings-page acceptance criterion), consolidated from what used to be
/// spread across <c>MainWindow.xaml.cs</c>'s code-behind. Numeric fields are string-backed (mirrors
/// the original `TextBox`-driven design) so invalid input doesn't crash a binding — validation
/// only happens when <see cref="SaveCommand"/> runs, exactly like the code-behind it replaces.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly IDeviceController _deviceController;
    private readonly IStartupRegistration _startupRegistration;

    // Guards the initial load-from-settings pass so setting StartWithWindows from persisted/live
    // state doesn't re-trigger Enable()/Disable() on the registry.
    private bool _isLoading;

    private string _restingSampleRateHzText = string.Empty;
    private string _restingBitDepthText = string.Empty;
    private string _gracePeriodSecondsText = string.Empty;
    private bool _isFollowDefaultMode = true;
    private bool _isPinnedMode;
    private string? _selectedDeviceId;
    private string _lossyStereoHzText = string.Empty;
    private string _losslessHzText = string.Empty;
    private string _hiResLosslessHzText = string.Empty;
    private string _dolbyAtmosHzText = string.Empty;
    private bool _startWithWindows;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(SettingsManager settingsManager, IDeviceController deviceController, IStartupRegistration startupRegistration)
    {
        _settingsManager = settingsManager;
        _deviceController = deviceController;
        _startupRegistration = startupRegistration;

        SaveCommand = new RelayCommand(Save);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        LoadFromSettings();
    }

    public ObservableCollection<RenderDevice> AvailableDevices { get; } = [];

    public string RestingSampleRateHzText
    {
        get => _restingSampleRateHzText;
        set => SetField(ref _restingSampleRateHzText, value);
    }

    public string RestingBitDepthText
    {
        get => _restingBitDepthText;
        set => SetField(ref _restingBitDepthText, value);
    }

    public string GracePeriodSecondsText
    {
        get => _gracePeriodSecondsText;
        set => SetField(ref _gracePeriodSecondsText, value);
    }

    public bool IsFollowDefaultMode
    {
        get => _isFollowDefaultMode;
        set => SetField(ref _isFollowDefaultMode, value);
    }

    public bool IsPinnedMode
    {
        get => _isPinnedMode;
        set => SetField(ref _isPinnedMode, value);
    }

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set => SetField(ref _selectedDeviceId, value);
    }

    public string LossyStereoHzText
    {
        get => _lossyStereoHzText;
        set => SetField(ref _lossyStereoHzText, value);
    }

    public string LosslessHzText
    {
        get => _losslessHzText;
        set => SetField(ref _losslessHzText, value);
    }

    public string HiResLosslessHzText
    {
        get => _hiResLosslessHzText;
        set => SetField(ref _hiResLosslessHzText, value);
    }

    public string DolbyAtmosHzText
    {
        get => _dolbyAtmosHzText;
        set => SetField(ref _dolbyAtmosHzText, value);
    }

    /// <summary>
    /// Applies immediately rather than waiting for <see cref="SaveCommand"/>, since this toggle
    /// drives the Run-key registration directly (see <see cref="IStartupRegistration"/>) rather
    /// than a persisted Setting — there's no "unsaved" state for it to sit in.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetField(ref _startWithWindows, value) || _isLoading)
            {
                return;
            }

            if (value)
            {
                _startupRegistration.Enable();
            }
            else
            {
                _startupRegistration.Disable();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    private void LoadFromSettings()
    {
        _isLoading = true;
        try
        {
            var settings = _settingsManager.Current;
            RestingSampleRateHzText = settings.RestingFormat.SampleRateHz.ToString();
            RestingBitDepthText = settings.RestingFormat.BitDepth.ToString();
            GracePeriodSecondsText = settings.GracePeriod.TotalSeconds.ToString("0");

            IsFollowDefaultMode = settings.DeviceTargetingMode == DeviceTargetingMode.FollowDefault;
            IsPinnedMode = settings.DeviceTargetingMode == DeviceTargetingMode.Pinned;

            var tierSampleRates = settings.EffectiveTierSampleRates;
            LossyStereoHzText = tierSampleRates.LossyStereoHz.ToString();
            LosslessHzText = tierSampleRates.LosslessHz.ToString();
            HiResLosslessHzText = tierSampleRates.HiResLosslessHz.ToString();
            DolbyAtmosHzText = tierSampleRates.DolbyAtmosHz.ToString();

            RefreshDevices();
            if (settings.PinnedDeviceId is not null)
            {
                SelectedDeviceId = settings.PinnedDeviceId;
            }

            // The Run-key registration (not a Settings field) is the source of truth for whether
            // Start-with-Windows is enabled — see IStartupRegistration — so this reads it live
            // rather than from persisted Settings.
            StartWithWindows = _startupRegistration.IsEnabled;

            StatusMessage = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Repopulates the device picker from the render devices Windows currently reports as active, preserving the current selection.</summary>
    private void RefreshDevices()
    {
        var previouslySelected = SelectedDeviceId;

        AvailableDevices.Clear();
        foreach (var device in _deviceController.GetActiveRenderDevices())
        {
            AvailableDevices.Add(device);
        }

        if (previouslySelected is not null)
        {
            SelectedDeviceId = previouslySelected;
        }
    }

    private void Save()
    {
        if (!int.TryParse(RestingSampleRateHzText, out var sampleRateHz) || sampleRateHz <= 0 ||
            !int.TryParse(RestingBitDepthText, out var bitDepth) || bitDepth <= 0 ||
            !double.TryParse(GracePeriodSecondsText, out var gracePeriodSeconds) || gracePeriodSeconds < 0)
        {
            StatusMessage = "Invalid values — not saved.";
            return;
        }

        if (!TryParseTierSampleRates(out var tierSampleRates))
        {
            StatusMessage = "Invalid tier sample rates — not saved.";
            return;
        }

        var targetingMode = IsPinnedMode ? DeviceTargetingMode.Pinned : DeviceTargetingMode.FollowDefault;
        if (targetingMode == DeviceTargetingMode.Pinned && SelectedDeviceId is null)
        {
            StatusMessage = "Pick a device to pin — not saved.";
            return;
        }

        _settingsManager.UpdateRestingFormat(new DeviceFormat(sampleRateHz, bitDepth));
        _settingsManager.UpdateGracePeriod(TimeSpan.FromSeconds(gracePeriodSeconds));
        _settingsManager.UpdateDeviceTargeting(targetingMode, SelectedDeviceId);
        _settingsManager.UpdateTierSampleRates(tierSampleRates);
        _deviceController.SetTargeting(targetingMode, SelectedDeviceId);
        StatusMessage = "Saved.";
    }

    private bool TryParseTierSampleRates(out TierSampleRateMapping tierSampleRates)
    {
        if (int.TryParse(LossyStereoHzText, out var lossyStereoHz) && lossyStereoHz > 0 &&
            int.TryParse(LosslessHzText, out var losslessHz) && losslessHz > 0 &&
            int.TryParse(HiResLosslessHzText, out var hiResLosslessHz) && hiResLosslessHz > 0 &&
            int.TryParse(DolbyAtmosHzText, out var dolbyAtmosHz) && dolbyAtmosHz > 0)
        {
            tierSampleRates = new TierSampleRateMapping(lossyStereoHz, losslessHz, hiResLosslessHz, dolbyAtmosHz);
            return true;
        }

        tierSampleRates = TierSampleRateMapping.Default;
        return false;
    }
}
