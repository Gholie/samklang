using System.Collections.ObjectModel;
using System.Globalization;
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
            RestingSampleRateHzText = settings.RestingFormat.SampleRateHz.ToString(CultureInfo.InvariantCulture);
            RestingBitDepthText = settings.RestingFormat.BitDepth.ToString(CultureInfo.InvariantCulture);
            GracePeriodSecondsText = settings.GracePeriod.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);

            IsFollowDefaultMode = settings.DeviceTargetingMode == DeviceTargetingMode.FollowDefault;
            IsPinnedMode = settings.DeviceTargetingMode == DeviceTargetingMode.Pinned;

            var tierSampleRates = settings.EffectiveTierSampleRates;
            LossyStereoHzText = tierSampleRates.LossyStereoHz.ToString(CultureInfo.InvariantCulture);
            LosslessHzText = tierSampleRates.LosslessHz.ToString(CultureInfo.InvariantCulture);
            HiResLosslessHzText = tierSampleRates.HiResLosslessHz.ToString(CultureInfo.InvariantCulture);
            DolbyAtmosHzText = tierSampleRates.DolbyAtmosHz.ToString(CultureInfo.InvariantCulture);

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
        // Invariant culture on every numeric parse (and the matching LoadFromSettings formats):
        // the current culture would reject "2.5" in comma-decimal locales even though this view
        // itself displayed the value with a dot.
        if (!TryParsePositiveInt(RestingSampleRateHzText, out var sampleRateHz) ||
            !TryParsePositiveInt(RestingBitDepthText, out var bitDepth) ||
            !double.TryParse(GracePeriodSecondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var gracePeriodSeconds) ||
            gracePeriodSeconds < 0)
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

        _settingsManager.UpdateFromSettingsView(
            new DeviceFormat(sampleRateHz, bitDepth),
            TimeSpan.FromSeconds(gracePeriodSeconds),
            targetingMode,
            SelectedDeviceId,
            tierSampleRates);
        _deviceController.SetTargeting(targetingMode, SelectedDeviceId);
        StatusMessage = "Saved.";
    }

    private bool TryParseTierSampleRates(out TierSampleRateMapping tierSampleRates)
    {
        if (TryParsePositiveInt(LossyStereoHzText, out var lossyStereoHz) &&
            TryParsePositiveInt(LosslessHzText, out var losslessHz) &&
            TryParsePositiveInt(HiResLosslessHzText, out var hiResLosslessHz) &&
            TryParsePositiveInt(DolbyAtmosHzText, out var dolbyAtmosHz))
        {
            tierSampleRates = new TierSampleRateMapping(lossyStereoHz, losslessHz, hiResLosslessHz, dolbyAtmosHz);
            return true;
        }

        tierSampleRates = TierSampleRateMapping.Default;
        return false;
    }

    private static bool TryParsePositiveInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}
