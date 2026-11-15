using CommunityToolkit.Mvvm.ComponentModel;

namespace HardHatMediaPlayer.ViewModels;

/// <summary>Picture shape: aspect ratio, cropping and filling the window. Remembered per folder.</summary>
public sealed partial class PlayerViewModel
{
    public static readonly string[] AspectRatios = { "16:9", "4:3", "21:9", "2.35:1", "1:1" };
    public static readonly string[] Crops = { "16:9", "4:3", "1.85:1", "2.35:1", "21:9" };

    /// <summary>Forced display aspect ratio ("16:9"), or null for the file's own.</summary>
    [ObservableProperty] private string? _aspectRatio;

    /// <summary>Crop to this ratio (e.g. cut the black bars burned into a 2.35:1 film in a 16:9 file), or null.</summary>
    [ObservableProperty] private string? _crop;

    /// <summary>Zoom until the picture covers the whole video area, cutting the edges instead of showing black bars.</summary>
    [ObservableProperty] private bool _fillWindow;

    private double _viewWidth, _viewHeight;
    private bool _quietPicture;

    /// <summary>The video area's size, for <see cref="FillWindow"/>.</summary>
    public void SetViewSize(double width, double height)
    {
        _viewWidth = width;
        _viewHeight = height;
        if (FillWindow) ApplyPicture();
    }

    private void ApplyPicture()
    {
        Player.AspectRatio = ForVlc(AspectRatio);
        // Filling the window is cropping to the window's own shape.
        Player.CropGeometry = FillWindow && _viewWidth >= 1 && _viewHeight >= 1 ? $"{(int)_viewWidth}:{(int)_viewHeight}" : ForVlc(Crop);
    }

    /// <summary>libVLC 3 reads ratios as whole numbers: "2.35:1" has to be "235:100" (as in VLC's own menus).</summary>
    private static string? ForVlc(string? ratio)
    {
        if (ratio is null || !ratio.Contains('.')) return ratio;
        var parts = ratio.Split(':');
        return decimal.TryParse(parts[0], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? $"{(int)(n * 100)}:{(parts.Length > 1 ? parts[1] : "1")}00"
            : ratio;
    }

    public void CycleAspectRatio() =>
        AspectRatio = AspectRatio is null ? AspectRatios[0]
            : System.Array.IndexOf(AspectRatios, AspectRatio) is var i && i + 1 < AspectRatios.Length ? AspectRatios[i + 1] : null;

    /// <summary>None, each crop ratio, then Fill window, then None again.</summary>
    public void CycleCrop()
    {
        if (FillWindow) { FillWindow = false; return; }
        var i = Crop is null ? -1 : System.Array.IndexOf(Crops, Crop);
        if (i + 1 < Crops.Length) Crop = Crops[i + 1];
        else FillWindow = true;
    }

    partial void OnAspectRatioChanged(string? value)
    {
        ApplyPicture();
        if (_quietPicture) return;
        Remember(p => p.AspectRatio = value);
        ShowOsd(value is null ? "Aspect ratio: default" : $"Aspect ratio: {value}", "Shift+A", "Video");
    }

    partial void OnCropChanged(string? value)
    {
        if (value != null && FillWindow) SetFillQuietly(false);
        ApplyPicture();
        if (_quietPicture) return;
        Remember(p => { p.Crop = value; p.FillWindow = false; });
        ShowOsd(value is null ? "Crop: none" : $"Crop: {value}", "Shift+C", "Video");
    }

    partial void OnFillWindowChanged(bool value)
    {
        if (value && Crop != null) { _quietPicture = true; try { Crop = null; } finally { _quietPicture = false; } }
        ApplyPicture();
        if (_quietPicture) return;
        Remember(p => { p.FillWindow = value; p.Crop = null; });
        ShowOsd(value ? "Fill window: on (edges are cut)" : "Fill window: off", "Shift+C", "Fullscreen");
    }

    private void SetFillQuietly(bool value)
    {
        _quietPicture = true;
        try { FillWindow = value; } finally { _quietPicture = false; }
    }

    /// <summary>When an item starts: the folder's picture settings, or the file's own shape.</summary>
    private void ApplyPicturePreset()
    {
        var preset = CurrentPreset;
        _quietPicture = true;
        try
        {
            AspectRatio = preset?.AspectRatio;
            Crop = preset?.FillWindow == true ? null : preset?.Crop;
            FillWindow = preset?.FillWindow == true;
        }
        finally { _quietPicture = false; }
        ApplyPicture();
    }
}
