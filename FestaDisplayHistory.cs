using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using TKey = Terminal.Gui.Input.Key;

namespace FestaScenarioAnalyzer;

internal sealed class FestaDisplayHistory(
    string workspaceTitle,
    string panelKey,
    string panelTitle)
{
    const int DefaultHistoryLimit = 100;
    const int MaximumHistoryLimit = 1000;
    const string HistoryNotificationKey = "history";

    static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    readonly object gate = new();
    readonly List<Entry> entries = [];
    IApplication? application;
    Func<Key, bool>? showUnit;
    Action<Key>? evictUnit;
    Workspace? workspace;
    HistoryView? view;
    WorkspaceContent? liveSnapshot;
    Key? visibleKey;
    int historyLimit = DefaultHistoryLimit;
    int selectedIndex = -1;
    bool active;
    bool hasUnread;
    bool confirmed;
    bool panelAdmitted;

    internal readonly record struct Key(int SingleModeCharaId, int Turn);

    public bool IsVisible(Key key)
    {
        lock (gate) return active && visibleKey == key;
    }

    public void Initialize(
        IApplication targetApplication,
        Func<Key, bool> show,
        Action<Key> evict)
    {
        ArgumentNullException.ThrowIfNull(targetApplication);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(evict);
        var settings = LoadSettings();
        lock (gate)
        {
            application = targetApplication;
            showUnit = show;
            evictUnit = evict;
            if (workspace is not null)
                workspace.MouseEvent -= WorkspaceMouseEvent;
            workspace = null;
            view = null;
            entries.Clear();
            liveSnapshot = null;
            visibleKey = null;
            historyLimit = settings.HistoryLimit;
            selectedIndex = -1;
            hasUnread = false;
            confirmed = false;
            panelAdmitted = false;
            active = true;
        }
    }

    public bool ShouldShow(Key key)
    {
        lock (gate)
        {
            if (!active)
                return false;
            if (historyLimit == 0 ||
                selectedIndex < 0 ||
                selectedIndex >= entries.Count)
            {
                return true;
            }
            if (entries[selectedIndex].HistoryKey == key)
                return true;

            var retainedIndex = entries.FindIndex(entry => entry.HistoryKey == key);
            if (retainedIndex >= 0)
                return false;

            return !confirmed || selectedIndex == entries.Count - 1 ||
                entries.Count >= historyLimit && selectedIndex == 0;
        }
    }

    public void Track(Workspace target, Key key)
        => PublishOrTrack(target, key, snapshot: null, switchToWorkspace: false);

    public void Show(
        Workspace target,
        Key key,
        WorkspaceContent snapshot,
        bool switchToWorkspace)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PublishOrTrack(target, key, snapshot, switchToWorkspace);
    }

    void PublishOrTrack(
        Workspace target,
        Key key,
        WorkspaceContent? snapshot,
        bool switchToWorkspace)
    {
        ArgumentNullException.ThrowIfNull(target);

        var commit = snapshot is not null;
        var evicted = new List<Key>();
        Action<Key>? evict;
        bool admitPanel;
        bool notifyUnread;
        var notifyNewest = false;
        Key? trackedSelection = null;
        Func<Key, bool>? show;
        string? positionNotification = null;
        lock (gate)
        {
            if (!active)
                return;

            if (!ReferenceEquals(workspace, target))
            {
                if (workspace is not null)
                    workspace.MouseEvent -= WorkspaceMouseEvent;
                workspace = target;
                workspace.MouseEvent += WorkspaceMouseEvent;
            }
            var previouslyVisible = visibleKey;
            if (historyLimit == 0)
            {
                entries.Clear();
                selectedIndex = -1;
                hasUnread = false;
                confirmed = false;
                notifyUnread = false;
            }
            else
            {
                Key? selectedKey = selectedIndex >= 0 && selectedIndex < entries.Count
                    ? entries[selectedIndex].HistoryKey
                    : null;
                var wasBrowsingOlder = selectedIndex >= 0 && selectedIndex < entries.Count - 1;
                var previouslyUnread = hasUnread;
                var index = entries.FindIndex(entry => entry.HistoryKey == key);
                var inserted = index < 0;
                if (inserted)
                    entries.Add(new(key));

                var overflow = entries.Count - historyLimit;
                if (overflow > 0)
                {
                    evicted.AddRange(entries.Take(overflow).Select(entry => entry.HistoryKey));
                    entries.RemoveRange(0, overflow);
                }

                if (commit)
                {
                    selectedIndex = entries.FindIndex(entry => entry.HistoryKey == key);
                    if (selectedIndex < 0)
                        throw new InvalidOperationException(FestaText.Get("HistoryCannotRetain", workspaceTitle, key));
                    if (selectedKey != key || selectedIndex == entries.Count - 1)
                        confirmed = false;
                    if (selectedIndex == entries.Count - 1)
                        hasUnread = false;
                    notifyUnread = false;
                }
                else
                {
                    var retainedSelection = selectedKey is { } currentKey
                        ? entries.FindIndex(entry => entry.HistoryKey == currentKey)
                        : -1;
                    if (selectedKey is not null && retainedSelection < 0)
                    {
                        selectedIndex = entries.Count - 1;
                        hasUnread = false;
                        confirmed = false;
                    }
                    else if (!wasBrowsingOlder || selectedKey is null || inserted && !confirmed)
                    {
                        selectedIndex = entries.Count - 1;
                        hasUnread = false;
                        confirmed = false;
                    }
                    else
                    {
                        selectedIndex = retainedSelection;
                        if (inserted)
                            hasUnread = true;
                        if (selectedIndex == entries.Count - 1)
                            hasUnread = false;
                    }

                    notifyUnread = inserted && wasBrowsingOlder && !previouslyUnread && hasUnread;
                    if (selectedIndex >= 0 && entries[selectedIndex].HistoryKey != visibleKey)
                        trackedSelection = entries[selectedIndex].HistoryKey;
                }
                notifyNewest = inserted && wasBrowsingOlder && selectedIndex == entries.Count - 1;
            }

            if (commit)
            {
                // 关闭历史或缩容后，旧可见快照可能已不在历史列表中。
                if (previouslyVisible is { } old && old != key && entries.All(entry => entry.HistoryKey != old))
                    evicted.Add(old);
                liveSnapshot = snapshot;
                visibleKey = key;
            }
            admitPanel = commit && !panelAdmitted;
            if (admitPanel)
                panelAdmitted = true;
            evict = evictUnit;
            show = showUnit;
            if (notifyNewest)
                positionNotification = PositionNotificationLocked();
        }

        foreach (var evictedKey in evicted.Distinct())
            if (evictedKey != key)
                evict?.Invoke(evictedKey);

        if (commit)
        {
            try
            {
                if (admitPanel)
                {
                    target.SetPanel(
                        panelKey,
                        panelTitle,
                        StablePanelContent,
                        fullBleed: true,
                        switchToWorkspace: switchToWorkspace);
                }
                else
                {
                    RefreshView();
                    if (switchToWorkspace && !ReferenceEquals(Workspace.Current, target))
                        target.SwitchTo();
                }
            }
            catch
            {
                if (admitPanel)
                {
                    lock (gate)
                        panelAdmitted = false;
                }
                throw;
            }
        }

        if (trackedSelection is { } targetKey && (show is null || !show(targetKey)))
            throw new InvalidOperationException(FestaText.Get("HistoryCannotShowSelected", workspaceTitle, targetKey));
        if (notifyUnread)
            target.Notify(HistoryNotificationKey, FestaText.Get("HistoryUnread"));
        else if (positionNotification is not null)
            target.Notify(HistoryNotificationKey, positionNotification);
    }

    public void Stop()
    {
        HistoryView? currentView;
        lock (gate)
        {
            active = false;
            entries.Clear();
            liveSnapshot = null;
            visibleKey = null;
            selectedIndex = -1;
            hasUnread = false;
            confirmed = false;
            panelAdmitted = false;
            currentView = view;
            view = null;
            if (workspace is not null)
                workspace.MouseEvent -= WorkspaceMouseEvent;
            workspace = null;
            showUnit = null;
            evictUnit = null;
            application = null;
        }
        currentView?.Deactivate();
    }

    public async Task ConfigPromptAsync(
        IApplication targetApplication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetApplication);
        cancellationToken.ThrowIfCancellationRequested();
        if (targetApplication.TopRunnable is null &&
            Environment.CurrentManagedThreadId != targetApplication.MainThreadId)
        {
            throw new InvalidOperationException(
                FestaText.Get("HistoryNoUiSession", workspaceTitle));
        }

        var draft = LoadSettings().HistoryLimit;
        int saved;
        if (Environment.CurrentManagedThreadId == targetApplication.MainThreadId)
        {
            saved = RunConfigDialog(targetApplication, draft, cancellationToken);
        }
        else
        {
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            targetApplication.Invoke(() =>
            {
                try
                {
                    completion.SetResult(RunConfigDialog(
                        targetApplication,
                        draft,
                        cancellationToken));
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            saved = await completion.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveSettings(new(saved));
        ApplyHistoryLimit(saved);
    }

    WorkspaceContent StablePanelContent
    {
        get
        {
            // The factory must create a fresh root for every Host realization.
            lock (gate)
                return stablePanelContent ??= new(CreateHistoryView);
        }
    }

    WorkspaceContent? stablePanelContent;

    void ApplyHistoryLimit(int value)
    {
        var evicted = new List<Key>();
        Key? showKey = null;
        Func<Key, bool>? show;
        Action<Key>? evict;
        Workspace? target;
        string? notification = null;
        lock (gate)
        {
            Key? selectedKey = selectedIndex >= 0 && selectedIndex < entries.Count
                ? entries[selectedIndex].HistoryKey
                : null;
            var wasBrowsingOlder = selectedIndex >= 0 && selectedIndex < entries.Count - 1;
            historyLimit = value;
            if (value == 0)
            {
                evicted.AddRange(entries
                    .Select(entry => entry.HistoryKey)
                    .Where(key => key != visibleKey));
                entries.Clear();
                selectedIndex = -1;
                hasUnread = false;
                confirmed = false;
            }
            else
            {
                var overflow = entries.Count - value;
                if (overflow > 0)
                {
                    evicted.AddRange(entries.Take(overflow).Select(entry => entry.HistoryKey));
                    entries.RemoveRange(0, overflow);
                }
                selectedIndex = selectedKey is { } currentKey
                    ? entries.FindIndex(entry => entry.HistoryKey == currentKey)
                    : -1;
                if (selectedIndex < 0 && entries.Count != 0)
                {
                    selectedIndex = entries.Count - 1;
                    hasUnread = false;
                    confirmed = false;
                }
                else if (selectedIndex == entries.Count - 1)
                {
                    hasUnread = false;
                    confirmed = false;
                }
                if (selectedIndex >= 0 && entries[selectedIndex].HistoryKey != visibleKey)
                    showKey = entries[selectedIndex].HistoryKey;
                if (wasBrowsingOlder && selectedIndex == entries.Count - 1)
                    notification = PositionNotificationLocked();
            }
            show = showUnit;
            evict = evictUnit;
            target = workspace;
        }

        foreach (var key in evicted.Distinct())
            if (key != showKey && key != visibleKey)
                evict?.Invoke(key);
        if (showKey is { } targetKey && (show is null || !show(targetKey)))
            throw new InvalidOperationException(FestaText.Get("HistoryCannotShowRetained", workspaceTitle, targetKey));
        if (notification is not null)
            target?.Notify(HistoryNotificationKey, notification);
    }

    bool TryNavigate(Navigation navigation)
    {
        Key? key = null;
        Func<Key, bool>? show;
        Workspace? target;
        string notification;
        lock (gate)
        {
            if (!active || historyLimit == 0 || entries.Count == 0)
                return false;
            if (navigation == Navigation.Confirm && (confirmed || selectedIndex == entries.Count - 1))
                return false;
            target = workspace;
            var previousIndex = selectedIndex;
            selectedIndex = navigation switch
            {
                Navigation.Older => Math.Max(0, selectedIndex - 1),
                Navigation.Newer => Math.Min(entries.Count - 1, selectedIndex + 1),
                Navigation.Oldest => 0,
                Navigation.Newest => entries.Count - 1,
                _ => selectedIndex,
            };
            if (navigation == Navigation.Confirm)
                confirmed = true;
            else if (selectedIndex != previousIndex || navigation == Navigation.Newest)
                confirmed = false;
            if (selectedIndex == entries.Count - 1)
                hasUnread = false;
            if (selectedIndex != previousIndex)
                key = entries[selectedIndex].HistoryKey;
            notification = PositionNotificationLocked();
            show = showUnit;
        }

        if (key is { } targetKey && (show is null || !show(targetKey)))
            throw new InvalidOperationException(FestaText.Get("HistoryCannotShowSelected", workspaceTitle, targetKey));
        target?.Notify(HistoryNotificationKey, notification);
        return true;
    }

    string PositionNotificationLocked()
    {
        var resource = selectedIndex == entries.Count - 1
            ? "HistoryNewest"
            : confirmed ? "HistoryConfirmed" : "HistoryPreview";
        return FestaText.Get(resource, selectedIndex + 1, entries.Count);
    }

    void WorkspaceMouseEvent(object? sender, Mouse mouse)
    {
        var flags = mouse.Flags;
        if (mouse.Handled || (flags & (MouseFlags.Ctrl | MouseFlags.Alt | MouseFlags.Shift)) != 0)
            return;
        for (var target = mouse.View; target is not null; target = target.SuperView)
            if (target is ScrollBar)
                return;

        Navigation? navigation = flags switch
        {
            MouseFlags.WheeledUp => Navigation.Older,
            MouseFlags.WheeledDown => Navigation.Newer,
            MouseFlags.LeftButtonClicked => Navigation.Confirm,
            MouseFlags.RightButtonClicked => Navigation.Newest,
            _ => null,
        };
        if (navigation is not { } requested)
            return;
        lock (gate)
        {
            if (!active || historyLimit == 0 || entries.Count == 0 ||
                requested == Navigation.Confirm && (confirmed || selectedIndex == entries.Count - 1))
                return;
            mouse.Handled = true;
        }
        TryNavigate(requested);
    }

    void RefreshView()
    {
        IApplication? targetApplication;
        lock (gate)
            targetApplication = active ? application : null;
        if (targetApplication is null)
            return;

        void Refresh()
        {
            HistoryView? currentView;
            WorkspaceContent? currentSnapshot;
            lock (gate)
            {
                if (!active)
                    return;
                currentView = view;
                // 队列等待期间可能已经浏览了另一条历史，以当前选择为准。
                currentSnapshot = liveSnapshot;
            }
            if (currentSnapshot is not null)
                currentView?.SetContent(currentSnapshot);
        }

        if (Environment.CurrentManagedThreadId == targetApplication.MainThreadId)
        {
            Refresh();
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        targetApplication.Invoke(() =>
        {
            try
            {
                Refresh();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        completion.Task
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
    }

    View CreateHistoryView()
    {
        IApplication targetApplication;
        lock (gate)
        {
            targetApplication = application
                ?? throw new InvalidOperationException(FestaText.Get("HistoryNotInitialized", workspaceTitle));
        }
        return new HistoryView(this, targetApplication);
    }

    bool AttachHistoryView(HistoryView candidate, out WorkspaceContent? snapshot)
    {
        lock (gate)
        {
            snapshot = VisibleSnapshotLocked();
            if (!active)
                return false;
            view = candidate;
            return true;
        }
    }

    void DetachHistoryView(HistoryView candidate)
    {
        lock (gate)
        {
            if (ReferenceEquals(view, candidate))
                view = null;
        }
    }

    bool CanNavigate(HistoryView candidate, IApplication targetApplication)
    {
        lock (gate)
        {
            if (!active || historyLimit == 0 ||
                !ReferenceEquals(Workspace.Current, workspace))
            {
                return false;
            }
        }

        for (var focused = targetApplication.TopRunnableView?.MostFocused;
             focused is not null;
             focused = focused.SuperView)
        {
            if (ReferenceEquals(focused, candidate))
                return true;
        }
        return false;
    }

    bool CanFocusHistory(IApplication targetApplication)
    {
        lock (gate)
            return active && ReferenceEquals(Workspace.Current, workspace) &&
                ReferenceEquals(targetApplication, application);
    }

    WorkspaceContent? VisibleSnapshotLocked() => liveSnapshot;

    static HistorySettings LoadSettings()
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
            return new(DefaultHistoryLimit);

        HistorySettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<HistorySettings>(
                    File.ReadAllText(path),
                    SettingsJson)
                ?? throw new JsonException(FestaText.Get("HistoryNullSettings"));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                FestaText.Get("HistoryInvalidSettings", path, ex.Message),
                ex);
        }
        ValidateHistoryLimit(settings.HistoryLimit, path);
        return settings;
    }

    static void SaveSettings(HistorySettings settings)
    {
        ValidateHistoryLimit(settings.HistoryLimit, SettingsFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        File.WriteAllText(
            SettingsFilePath,
            JsonSerializer.Serialize(settings, SettingsJson));
    }

    static void ValidateHistoryLimit(int value, string source)
    {
        if (value is < 0 or > MaximumHistoryLimit)
        {
            throw new InvalidDataException(
                FestaText.Get("HistoryInvalidLimit", MaximumHistoryLimit, source, value));
        }
    }

    static int RunConfigDialog(
        IApplication application,
        int draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new Dialog
        {
            Title = FestaText.Get("SettingsTitle"),
            Width = 62,
            Height = 10,
        };
        var input = new TextField
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Text = draft.ToString(CultureInfo.InvariantCulture),
        };
        var validation = new Label
        {
            X = 1,
            Y = 4,
            Width = Dim.Fill(1),
            Height = 2,
        };
        dialog.Add(
            new Label { X = 1, Y = 1, Text = FestaText.Get("HistoryLimitLabel", MaximumHistoryLimit) },
            input,
            validation);

        var accepted = false;
        var result = draft;
        var save = new Button { Text = FestaText.Get("Save"), IsDefault = true };
        save.Accepting += (_, e) =>
        {
            if (!int.TryParse(
                    input.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out result) || result is < 0 or > MaximumHistoryLimit)
            {
                validation.Text = FestaText.Get("HistoryLimitValidation", MaximumHistoryLimit);
                e.Handled = true;
                return;
            }

            accepted = true;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = FestaText.Get("Cancel") };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(cancel);
        dialog.AddButton(save);
        input.SetFocus();

        using (cancellationToken.Register(
                   () => application.Invoke(() => application.RequestStop(dialog))))
            application.Run(dialog);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accepted)
        {
            throw new OperationCanceledException(
                FestaText.Get("SettingsCancelled"),
                cancellationToken);
        }
        return result;
    }

    sealed class HistoryView : View
    {
        readonly FestaDisplayHistory owner;
        readonly IApplication application;
        View? content;
        bool active;

        internal HistoryView(FestaDisplayHistory owner, IApplication application)
        {
            this.owner = owner;
            this.application = application;
            Id = "festa-history-root";
            Width = Dim.Fill();
            Height = Dim.Fill();
            CanFocus = true;
            TabStop = TabBehavior.TabGroup;
            active = owner.AttachHistoryView(this, out var initialSnapshot);
            if (active)
                application.Keyboard.KeyDown += ApplicationKeyDown;
            if (initialSnapshot is not null)
                SetContent(initialSnapshot);
            Initialized += (_, _) =>
            {
                if (owner.CanFocusHistory(application))
                    SetFocus();
            };
        }

        internal void SetContent(WorkspaceContent snapshot)
        {
            var focused = Contains(application.TopRunnableView?.MostFocused);
            var next = snapshot.CreateView();
            next.Width = Dim.Fill();
            next.Height = Dim.Fill();
            if (content is not null)
            {
                Remove(content);
                content.Dispose();
            }
            content = next;
            Add(next);
            SetNeedsLayout();
            SetNeedsDraw();
            if (focused)
                SetFocus();
        }

        internal void Deactivate()
        {
            if (!active)
                return;
            active = false;
            application.Keyboard.KeyDown -= ApplicationKeyDown;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Deactivate();
                owner.DetachHistoryView(this);
            }
            base.Dispose(disposing);
        }

        void ApplicationKeyDown(object? sender, TKey key)
        {
            if (!active || key.Handled || key.IsCtrl || key.IsAlt || key.IsShift ||
                !owner.CanNavigate(this, application))
            {
                return;
            }

            Command? contentNavigation = key.KeyCode switch
            {
                var code when code == TKey.PageUp.KeyCode => Command.PageUp,
                var code when code == TKey.PageDown.KeyCode => Command.PageDown,
                var code when code == TKey.Home.KeyCode => Command.Start,
                var code when code == TKey.End.KeyCode => Command.End,
                _ => null,
            };
            if (contentNavigation is { } command && content is not null &&
                FestaDisplayRenderer.TryScroll(content, command))
            {
                key.Handled = true;
                return;
            }

            Navigation? navigation = key.KeyCode switch
            {
                var code when code == TKey.CursorUp.KeyCode => Navigation.Older,
                var code when code == TKey.CursorDown.KeyCode => Navigation.Newer,
                var code when code == TKey.CursorLeft.KeyCode => Navigation.Oldest,
                var code when code == TKey.CursorRight.KeyCode => Navigation.Newest,
                _ => null,
            };
            if (navigation is { } requested && owner.TryNavigate(requested))
                key.Handled = true;
        }

        bool Contains(View? focused)
        {
            for (; focused is not null; focused = focused.SuperView)
            {
                if (ReferenceEquals(focused, this))
                    return true;
            }
            return false;
        }
    }

    static string SettingsFilePath
        => Path.Combine("PluginData", "FestaScenarioAnalyzer", "settings.json");

    sealed record HistorySettings(
        [property: JsonRequired]
        int HistoryLimit);

    readonly record struct Entry(Key HistoryKey);

    enum Navigation
    {
        Older,
        Newer,
        Oldest,
        Newest,
        Confirm,
    }
}
