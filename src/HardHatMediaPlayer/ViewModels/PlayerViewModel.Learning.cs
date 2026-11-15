using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using HardHatMediaPlayer.Services;

namespace HardHatMediaPlayer.ViewModels;

/// <summary>
/// Tools for language learners: a second (translation) subtitle drawn at the top of the video, line-by-line
/// navigation and pausing after each line. libVLC draws the main subtitle; these read subtitle files directly.
/// </summary>
public sealed partial class PlayerViewModel
{
    private DispatcherTimer? _learnTimer;
    private List<SubtitleCue> _secondCues = new();
    private string? _lineFile;
    private List<SubtitleCue> _lineCues = new();
    private int _insideLine = -1;
    private int _pausedAfterLine = -1;

    /// <summary>The file shown as the second subtitle, or null.</summary>
    [ObservableProperty] private string? _secondSubtitleFile;

    /// <summary>The translation line on screen now ("" between lines).</summary>
    [ObservableProperty] private string _secondSubtitleText = "";

    /// <summary>Blur the translation until the pointer is over it, so you try the other language first.</summary>
    [ObservableProperty] private bool _blurTranslation;

    /// <summary>Pause at the end of every subtitle line.</summary>
    [ObservableProperty] private bool _autoPause;

    private void InitLearning()
    {
        // Runs from the constructor, before the window listens for on-screen messages.
        BlurTranslation = _store.Data.BlurTranslation;
        _learnTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Normal, (_, _) => LearnTick(), _ui);
    }

    /// <summary>A new item starts: nothing from the previous one carries over.</summary>
    private void ResetLearning()
    {
        _secondCues = new List<SubtitleCue>();
        SecondSubtitleFile = null;
        SecondSubtitleText = "";
        _lineFile = null;
        _lineCues = new List<SubtitleCue>();
        _insideLine = _pausedAfterLine = -1;
        UpdateLearnTimer();
    }

    private void UpdateLearnTimer()
    {
        if (_learnTimer is null) return;
        if (HasMedia && (_secondCues.Count > 0 || AutoPause)) _learnTimer.Start();
        else _learnTimer.Stop();
    }

    /// <summary>Subtitle time: a positive subtitle delay shows every line later.</summary>
    private long CueTime => Player.Time - SubtitleDelayMs;

    private void LearnTick()
    {
        if (!HasMedia || _disposed) return;
        if (IsEnded) { SecondSubtitleText = ""; return; }
        var t = CueTime;

        if (_secondCues.Count > 0)
        {
            var i = SubtitleFile.IndexAt(_secondCues, t);
            SecondSubtitleText = i >= 0 ? _secondCues[i].Text : "";
        }

        if (AutoPause && IsPlaying && LineCues() is { Count: > 0 } lines)
        {
            // Pause once when the line we were in reaches its end (a little early: the timer ticks every 80 ms).
            // The upper bound skips lines left behind by a seek.
            if (_insideLine >= 0 && _insideLine < lines.Count && _insideLine != _pausedAfterLine
                && t >= lines[_insideLine].EndMs - 60 && t < lines[_insideLine].EndMs + 500)
            {
                _pausedAfterLine = _insideLine;
                Player.SetPause(true);
                ShowOsd("Paused after the line", "Space continue  ·  R replay  ·  E turn off", "Pause");
            }
            _insideLine = SubtitleFile.IndexAt(lines, t);
        }
    }

    // ---- Second subtitle ----

    /// <summary>Subtitle files that can be the second subtitle for what's playing.</summary>
    public IReadOnlyList<string> SecondSubtitleCandidates =>
        Kind == MediaKind.Stream || CurrentPath is null ? Array.Empty<string>() : SubtitleFile.FindFor(CurrentPath);

    /// <summary>Shows a subtitle file at the top (null hides it) and remembers its language for this folder.</summary>
    public void SetSecondSubtitle(string? file)
    {
        if (!HasMedia) return;
        if (!LoadSecond(file)) { ShowMessage("Couldn't read that subtitle file (.srt, .vtt, .ass or .ssa)."); return; }
        Remember(p => p.SecondSubtitleLanguage = file is null ? Off : SubtitleFile.LanguageOf(file) ?? Path.GetFileName(file));
        ShowOsd(file is null ? "Second subtitle off" : $"Second subtitle: {SubtitleFile.Label(file)}",
            "Shift+V switch  ·  C blur", "Subtitles");
    }

    public void CycleSecondSubtitle()
    {
        var files = SecondSubtitleCandidates.ToList();
        if (files.Count == 0)
        {
            ShowOsd("No subtitle files for a second subtitle", "Subtitles button → Second subtitle → Choose file", "Subtitles");
            return;
        }
        // Off, then each file in turn.
        var i = SecondSubtitleFile is null ? -1 : files.FindIndex(f => string.Equals(f, SecondSubtitleFile, StringComparison.OrdinalIgnoreCase));
        SetSecondSubtitle(i + 1 < files.Count ? files[i + 1] : null);
    }

    private bool LoadSecond(string? file)
    {
        var cues = file is null ? new List<SubtitleCue>() : SubtitleFile.Load(file);
        if (file != null && cues.Count == 0) return false;
        _secondCues = cues;
        SecondSubtitleFile = file;
        SecondSubtitleText = "";
        UpdateLearnTimer();
        return true;
    }

    /// <summary>When an item starts: the second subtitle in the language chosen for this folder, if a file has it.</summary>
    private void ApplySecondSubtitlePreset()
    {
        var wanted = CurrentPreset?.SecondSubtitleLanguage;
        if (wanted is null or Off) return;
        var file = SecondSubtitleCandidates.FirstOrDefault(f => SubtitleFile.LanguageOf(f) == wanted || Path.GetFileName(f) == wanted);
        if (file != null && LoadSecond(file))
            ShowOsd($"Second subtitle: {SubtitleFile.Label(file)}", "Remembered for this folder", "Subtitles");
    }

    partial void OnBlurTranslationChanged(bool value)
    {
        _store.Data.BlurTranslation = value;
        ShowOsd(value ? "Translation blurred · point at it to read" : "Translation always visible", "C", "Subtitles");
    }

    partial void OnAutoPauseChanged(bool value)
    {
        _insideLine = _pausedAfterLine = -1;
        UpdateLearnTimer();
        ShowOsd(value ? "Pause after each line: on" : "Pause after each line: off",
            value && LineCues().Count == 0 ? "Needs a subtitle file (.srt, .vtt, .ass)" : "E", "Pause");
    }

    // ---- Line by line ----

    /// <summary>
    /// The lines used to find "the current line": the subtitle file in the main subtitle's language, else the
    /// second subtitle, else any file for the video. libVLC doesn't expose the timing of embedded subtitles.
    /// </summary>
    private List<SubtitleCue> LineCues()
    {
        var files = SecondSubtitleCandidates;
        var mainLang = Player.Spu == -1 ? null : LanguageOf(TrackType.Text, Player.Spu, SubtitleTracks.FirstOrDefault(t => t.Id == Player.Spu).Name);
        var file = files.FirstOrDefault(f => mainLang != null && SubtitleFile.LanguageOf(f) == mainLang && f != SecondSubtitleFile)
            ?? SecondSubtitleFile
            ?? files.FirstOrDefault();
        if (file != _lineFile)
        {
            _lineFile = file;
            _lineCues = file == SecondSubtitleFile ? _secondCues : file is null ? new List<SubtitleCue>() : SubtitleFile.Load(file);
        }
        return _lineCues;
    }

    public void ReplayLine() => JumpToLine(0, "Replaying the line", "R");
    public void PreviousLine() => JumpToLine(-1, "Previous line", "A previous  ·  D next");
    public void NextLine() => JumpToLine(1, "Next line", "A previous  ·  D next");

    /// <param name="direction">0 replays the current (or last heard) line; -1 and 1 go to the line before or after.</param>
    private void JumpToLine(int direction, string what, string hint)
    {
        if (!HasMedia || !Player.IsSeekable) return;
        var lines = LineCues();
        if (lines.Count == 0)
        {
            ShowOsd("Line by line needs a subtitle file", "Add one: subtitles button → Add subtitle file or Find subtitles online", "Subtitles");
            return;
        }
        var t = CueTime;
        var last = SubtitleFile.IndexBefore(lines, t);
        // A line that just ended (e.g. paused after it) still counts as the current one.
        var inside = last >= 0 && t < lines[last].EndMs + 700;
        var target = direction switch
        {
            0 => Math.Max(last, 0),
            < 0 => inside ? last - 1 : last,
            _ => last + 1,
        };
        if (target < 0 || target >= lines.Count) { ShowOsd(direction < 0 ? "First line" : "Last line", hint, "Subtitles"); return; }

        var line = lines[target];
        var ms = Math.Max(0, line.StartMs + SubtitleDelayMs - 150);
        Player.Time = ms;
        var length = Player.Length;
        if (length > 0) Position = ms / (double)length;
        TimeText = Format(ms);
        _seekSettles = DateTime.Now.AddMilliseconds(700);
        _insideLine = target;
        _pausedAfterLine = -1;
        if (!Player.IsPlaying && Player.State == VLCState.Paused) Player.SetPause(false);
        ShowOsd(what, hint, direction < 0 ? "Rewind" : direction > 0 ? "Forward" : "Repeat");
    }
}
