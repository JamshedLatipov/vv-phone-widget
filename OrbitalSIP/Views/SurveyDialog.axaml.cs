using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class SurveyDialog : Window
    {
        private readonly FlowsService _svc = App.FlowsService;

        private readonly string _callerNumber;

        // When set (campaign call with a bound questionnaire), the dialog jumps
        // straight into this flow instead of showing the picker list.
        private readonly string? _autoFlowId;

        // run state
        private string? _subjectId;
        private string? _runId;
        private Dictionary<string, FlowNode> _graph = new();
        private string? _currentNodeKey;
        private int _stepCounter;
        private JsonElement? _runContext;

        // abandon
        private string? _selectedAbandonReason;

        // handler leak guard
        private EventHandler<TextChangedEventArgs>? _textChangedHandler;

        private static readonly string[] AbandonReasons =
        {
            "Клиент недоступен",
            "Отказ клиента",
            "Техническая проблема",
            "Другое"
        };

        public SurveyDialog(string callerNumber, string? autoFlowId = null)
        {
            _callerNumber = callerNumber;
            _autoFlowId = autoFlowId;
            InitializeComponent();
            WireStaticButtons();
            _ = InitAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        // ── Init: resolve uniqueid then load list ────────────────────────────

        private async Task InitAsync()
        {
            var uniqueId = await _svc.GetChannelUniqueIdAsync(_callerNumber);
            _subjectId = uniqueId ?? _callerNumber;

            if (uniqueId == null)
            {
                ShowHint("Не удалось определить звонок — используется номер как идентификатор");
            }

            // Campaign call with a bound questionnaire → resume an in-progress run
            // for this call if one exists, otherwise start the bound flow directly.
            if (!string.IsNullOrEmpty(_autoFlowId))
            {
                if (_subjectId != null)
                {
                    var runs = await _svc.ListRunsAsync(_subjectId);
                    var existing = runs.FirstOrDefault(
                        r => r.Status == "in_progress" && r.FlowId == _autoFlowId);
                    if (existing?.Id != null)
                    {
                        await ResumeRunAsync(existing.Id);
                        return;
                    }
                }
                await StartFlowAsync(_autoFlowId);
                return;
            }

            await LoadListAsync();
        }

        private void ShowHint(string msg)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var lbl = this.FindControl<TextBlock>("ErrorLabel");
                if (lbl == null) return;
                lbl.Text = msg;
                lbl.IsVisible = true;
            });
        }

        // ── List phase ───────────────────────────────────────────────────────

        private async Task LoadListAsync()
        {
            Dispatcher.UIThread.Post(() =>
            {
                Show("LoadingLabel", true);
                Show("EmptyLabel", false);
                Show("InProgressSection", false);
                Show("AvailableSection", false);
            });

            List<FlowRun> runs = new();
            List<FlowDefinition> flows = new();

            if (_subjectId != null)
                runs = await _svc.ListRunsAsync(_subjectId);

            flows = await _svc.ListFlowsAsync();

            Dispatcher.UIThread.Post(() =>
            {
                Show("LoadingLabel", false);

                var inProgress = runs.Where(r => r.Status == "in_progress").ToList();
                var available = flows.Where(f => f.IsActive && f.ActiveVersionId != null).ToList();

                if (!inProgress.Any() && !available.Any())
                {
                    Show("EmptyLabel", true);
                    return;
                }

                if (inProgress.Any())
                {
                    var list = this.FindControl<StackPanel>("InProgressList");
                    var section = this.FindControl<StackPanel>("InProgressSection");
                    if (list != null && section != null)
                    {
                        list.Children.Clear();
                        var flowNameById = flows.ToDictionary(f => f.Id ?? "", f => f.Name ?? "Анкета");
                        foreach (var run in inProgress)
                        {
                            var label = $"Продолжить: {flowNameById.GetValueOrDefault(run.FlowId ?? "", "Анкета")}";
                            var btn = MakeListButton(label, "#1E4270", "#60A5FA");
                            btn.Tag = run;
                            btn.Click += async (_, __) => await ResumeRunAsync(run.Id!);
                            list.Children.Add(btn);
                        }
                        section.IsVisible = true;
                    }
                }

                if (available.Any())
                {
                    var list = this.FindControl<StackPanel>("AvailableList");
                    var section = this.FindControl<StackPanel>("AvailableSection");
                    if (list != null && section != null)
                    {
                        list.Children.Clear();
                        foreach (var flow in available)
                        {
                            var btn = MakeListButton(flow.Name ?? flow.Id ?? "Анкета", "#1E293B", "#E2E8F0");
                            btn.Tag = flow;
                            btn.Click += async (_, __) => await StartFlowAsync(flow.Id!);
                            list.Children.Add(btn);
                        }
                        section.IsVisible = true;
                    }
                }
            });
        }

        private Button MakeListButton(string text, string bg, string fg)
        {
            return new Button
            {
                Content = text,
                Background = SolidColorBrush.Parse(bg),
                Foreground = SolidColorBrush.Parse(fg),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(10),
                Padding = new Avalonia.Thickness(12, 8),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                FontSize = 13
            };
        }

        // ── Start / Resume run ───────────────────────────────────────────────

        private async Task StartFlowAsync(string flowId)
        {
            SetBusy(true);
            var resp = await _svc.StartRunAsync(flowId, _subjectId!);
            SetBusy(false);

            if (resp?.Run == null || resp.Graph == null)
            {
                ShowHint("Не удалось запустить анкету");
                return;
            }

            ApplyRunState(resp.Run.Id!, resp.Graph, resp.Context, resp.CurrentNodeKey ?? resp.Graph.StartNodeKey, 1);
        }

        private async Task ResumeRunAsync(string runId)
        {
            SetBusy(true);
            var state = await _svc.GetRunStateAsync(runId);
            SetBusy(false);

            if (state?.Run == null || state.Graph == null)
            {
                ShowHint("Не удалось загрузить состояние анкеты");
                return;
            }

            var answeredCount = state.Answers?.Count(a => a.SupersededAt == null) ?? 0;
            ApplyRunState(state.Run.Id!, state.Graph, state.Run.Context, state.CurrentNodeKey, answeredCount + 1);
        }

        private void ApplyRunState(string runId, FlowGraph graph, JsonElement? context, string? nodeKey, int step)
        {
            _runId = runId;
            _graph = (graph.Nodes ?? new List<FlowNode>())
                .Where(n => n.Key != null)
                .ToDictionary(n => n.Key!);
            _runContext = context;
            _stepCounter = step;

            Dispatcher.UIThread.Post(() =>
            {
                Show("ListPhase", false);
                Show("WizardPhase", true);
                Show("ListFooter", false);
                Show("WizardFooter", true);
                Show("DoneBtn", false);
                Show("AbandonArea", false);

                RenderNode(nodeKey);
            });
        }

        // ── Wizard rendering ─────────────────────────────────────────────────

        private void RenderNode(string? nodeKey)
        {
            _currentNodeKey = nodeKey;

            if (nodeKey == null || !_graph.TryGetValue(nodeKey, out var node))
            {
                ShowCompleted(null);
                return;
            }

            if (node.Type == "end")
            {
                ShowCompleted(null);
                return;
            }

            // Step header
            SetTextBlock("StepLabel", $"Шаг {_stepCounter}");
            SetTextBlock("NodeTitle", node.Title ?? "");

            // Script
            var script = SubstituteVars(node.Script ?? "");
            var scriptBorder = this.FindControl<Border>("ScriptBorder");
            if (scriptBorder != null)
            {
                scriptBorder.IsVisible = !string.IsNullOrWhiteSpace(script);
                SetTextBlock("ScriptText", script);
            }

            // Hide all answer areas; also clear ButtonsArea children
            var buttonsArea = this.FindControl<StackPanel>("ButtonsArea");
            if (buttonsArea != null) { buttonsArea.IsVisible = false; buttonsArea.Children.Clear(); }
            Show("TextArea", false);
            Show("NumberArea", false);
            Show("CommentArea", false);
            Show("VerificationBanner", false);
            Show("CompletedArea", false);
            Show("WizardFooter", true);

            // Clear inputs
            var textBox = this.FindControl<TextBox>("TextAnswerBox");
            var numBox = this.FindControl<TextBox>("NumberAnswerBox");
            var commentBox = this.FindControl<TextBox>("CommentBox");
            if (textBox != null) textBox.Text = "";
            if (numBox != null) numBox.Text = "";
            if (commentBox != null) commentBox.Text = "";

            var answerType = node.AnswerType ?? "none";

            switch (answerType)
            {
                case "buttons":
                case "select":
                    RenderButtonOptions(node);
                    break;
                case "text":
                    Show("TextArea", true);
                    ConfigureNextBtn(node);
                    break;
                case "number":
                    Show("NumberArea", true);
                    ConfigureNextBtn(node);
                    break;
                default: // info / none
                    ConfigureNextBtn(node, forceEnabled: true);
                    break;
            }

            if (node.AllowComment == true)
                Show("CommentArea", true);

            // Назад enabled only when not on first step
            var backBtn = this.FindControl<Button>("BackBtn");
            if (backBtn != null) backBtn.IsEnabled = _stepCounter > 1;
        }

        private void RenderButtonOptions(FlowNode node)
        {
            var area = this.FindControl<StackPanel>("ButtonsArea");
            if (area == null) return;

            area.Children.Clear();
            area.IsVisible = true;

            var options = node.Options ?? new List<FlowNodeOption>();
            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Content = opt.Label ?? opt.Value ?? "",
                    Tag = opt.Value,
                    Background = SolidColorBrush.Parse("#1E293B"),
                    Foreground = SolidColorBrush.Parse("#E2E8F0"),
                    BorderThickness = new Avalonia.Thickness(1),
                    BorderBrush = SolidColorBrush.Parse("#334155"),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(12, 8),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    FontSize = 13
                };
                btn.Click += async (_, __) =>
                {
                    var comment = this.FindControl<TextBox>("CommentBox")?.Text?.Trim();
                    await SubmitAnswerAsync(opt.Value, string.IsNullOrEmpty(comment) ? null : comment);
                };
                area.Children.Add(btn);
            }

            // Hide Next for button/select — clicking option submits directly
            var nextBtn = this.FindControl<Button>("NextBtn");
            if (nextBtn != null) nextBtn.IsVisible = false;
        }

        private void ConfigureNextBtn(FlowNode node, bool forceEnabled = false)
        {
            var nextBtn = this.FindControl<Button>("NextBtn");
            if (nextBtn == null) return;

            nextBtn.IsVisible = true;

            bool required = node.Required != false; // default true if not specified
            if (forceEnabled || !required)
            {
                nextBtn.IsEnabled = true;
                return;
            }

            // Wire up enable/disable based on input
            nextBtn.IsEnabled = false;

            var answerType = node.AnswerType ?? "none";
            if (answerType == "text")
            {
                var box = this.FindControl<TextBox>("TextAnswerBox");
                if (box != null)
                {
                    if (_textChangedHandler != null) box.TextChanged -= _textChangedHandler;
                    _textChangedHandler = (_, __) => { if (nextBtn != null) nextBtn.IsEnabled = !string.IsNullOrWhiteSpace(box.Text); };
                    box.TextChanged += _textChangedHandler;
                }
            }
            else if (answerType == "number")
            {
                var box = this.FindControl<TextBox>("NumberAnswerBox");
                if (box != null)
                {
                    if (_textChangedHandler != null) box.TextChanged -= _textChangedHandler;
                    _textChangedHandler = (_, __) => { if (nextBtn != null) nextBtn.IsEnabled = !string.IsNullOrWhiteSpace(box.Text); };
                    box.TextChanged += _textChangedHandler;
                }
            }
        }

        private async Task SubmitAnswerAsync(string? value, string? comment)
        {
            if (_runId == null || _currentNodeKey == null) return;

            SetWizardBusy(true);

            var resp = await _svc.AnswerAsync(_runId, _currentNodeKey, value, comment);

            if (resp == null)
            {
                // 409 or error — reload state
                var state = await _svc.GetRunStateAsync(_runId);
                SetWizardBusy(false);
                if (state == null) return;
                _runContext = state.Run?.Context;
                var answeredCount = state.Answers?.Count(a => a.SupersededAt == null) ?? 0;
                _stepCounter = answeredCount + 1;
                Dispatcher.UIThread.Post(() => RenderNode(state.CurrentNodeKey));
                return;
            }

            SetWizardBusy(false);

            // Show verification banner before moving
            if (!string.IsNullOrEmpty(resp.VerificationResult))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var banner = this.FindControl<Border>("VerificationBanner");
                    var text = this.FindControl<TextBlock>("VerificationText");
                    if (banner == null || text == null) return;
                    banner.IsVisible = true;
                    if (resp.VerificationResult == "mismatch")
                    {
                        text.Text = "Несоответствие данных";
                        banner.BorderBrush = SolidColorBrush.Parse("#78350F");
                        text.Foreground = SolidColorBrush.Parse("#FCD34D");
                    }
                    else
                    {
                        text.Text = "Нет данных для сверки";
                        banner.BorderBrush = SolidColorBrush.Parse("#1E3A5F");
                        text.Foreground = SolidColorBrush.Parse("#93C5FD");
                    }
                });
            }

            if (resp.Completed)
            {
                Dispatcher.UIThread.Post(() => ShowCompleted(resp.Verdict));
                return;
            }

            _stepCounter++;
            Dispatcher.UIThread.Post(() => RenderNode(resp.NextNodeKey));
        }

        private void ShowCompleted(JsonElement? verdict)
        {
            Show("WizardFooter", false);
            Show("AbandonArea", false);
            Show("CompletedArea", true);
            Show("DoneBtn", true);
            Show("ButtonsArea", false);
            Show("TextArea", false);
            Show("NumberArea", false);
            Show("CommentArea", false);

            if (verdict.HasValue && verdict.Value.ValueKind != JsonValueKind.Null)
            {
                var verdictText = this.FindControl<TextBlock>("VerdictText");
                if (verdictText != null)
                {
                    try { verdictText.Text = verdict.Value.GetRawText(); }
                    catch { verdictText.Text = ""; }
                }
            }
        }

        // ── Variable substitution ────────────────────────────────────────────

        private string SubstituteVars(string template)
        {
            if (!_runContext.HasValue || string.IsNullOrEmpty(template))
                return template;

            return Regex.Replace(template, @"\{\{([^}]+)\}\}", m =>
            {
                var path = m.Groups[1].Value.Trim();
                return ResolveContextPath(path) ?? "";
            });
        }

        private string? ResolveContextPath(string dotPath)
        {
            if (!_runContext.HasValue) return null;
            var parts = dotPath.Split('.');
            JsonElement el = _runContext.Value;
            foreach (var part in parts)
            {
                if (el.ValueKind != JsonValueKind.Object) return null;
                if (!el.TryGetProperty(part, out el)) return null;
            }
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        }

        // ── Wiring ──────────────────────────────────────────────────────────

        private void WireStaticButtons()
        {
            var closeBtn = this.FindControl<Button>("CloseBtn");
            if (closeBtn != null) closeBtn.Click += (_, __) => Close();

            var doneBtn = this.FindControl<Button>("DoneBtn");
            if (doneBtn != null) doneBtn.Click += (_, __) => Close();

            var nextBtn = this.FindControl<Button>("NextBtn");
            if (nextBtn != null)
                nextBtn.Click += async (_, __) => await OnNextClicked();

            var backBtn = this.FindControl<Button>("BackBtn");
            if (backBtn != null)
                backBtn.Click += async (_, __) => await OnBackClicked();

            var abandonBtn = this.FindControl<Button>("AbandonBtn");
            if (abandonBtn != null)
                abandonBtn.Click += (_, __) => ShowAbandonPicker();

            var abandonCancelBtn = this.FindControl<Button>("AbandonCancelBtn");
            if (abandonCancelBtn != null)
                abandonCancelBtn.Click += (_, __) => HideAbandonPicker();

            var abandonConfirmBtn = this.FindControl<Button>("AbandonConfirmBtn");
            if (abandonConfirmBtn != null)
                abandonConfirmBtn.Click += async (_, __) => await ConfirmAbandonAsync();

            BuildAbandonReasonButtons();
        }

        private void BuildAbandonReasonButtons()
        {
            var list = this.FindControl<StackPanel>("AbandonReasonList");
            if (list == null) return;

            foreach (var reason in AbandonReasons)
            {
                var btn = new Button
                {
                    Content = reason,
                    Background = SolidColorBrush.Parse("#1E293B"),
                    Foreground = SolidColorBrush.Parse("#E2E8F0"),
                    BorderThickness = new Avalonia.Thickness(1),
                    BorderBrush = SolidColorBrush.Parse("#334155"),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(10, 6),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    FontSize = 12
                };
                btn.Click += (_, __) => SelectAbandonReason(reason, btn);
                list.Children.Add(btn);
            }
        }

        private void SelectAbandonReason(string reason, Button selected)
        {
            _selectedAbandonReason = reason;
            var list = this.FindControl<StackPanel>("AbandonReasonList");
            if (list == null) return;

            foreach (var child in list.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = btn == selected
                        ? SolidColorBrush.Parse("#1E4270")
                        : SolidColorBrush.Parse("#1E293B");
                    btn.BorderBrush = btn == selected
                        ? SolidColorBrush.Parse("#3B82F6")
                        : SolidColorBrush.Parse("#334155");
                }
            }
        }

        private void ShowAbandonPicker()
        {
            _selectedAbandonReason = null;
            Show("WizardFooter", false);
            Show("AbandonArea", true);

            // Reset selection visual
            var list = this.FindControl<StackPanel>("AbandonReasonList");
            if (list != null)
                foreach (var child in list.Children)
                    if (child is Button btn)
                    {
                        btn.Background = SolidColorBrush.Parse("#1E293B");
                        btn.BorderBrush = SolidColorBrush.Parse("#334155");
                    }
        }

        private void HideAbandonPicker()
        {
            Show("AbandonArea", false);
            Show("WizardFooter", true);
        }

        private async Task ConfirmAbandonAsync()
        {
            if (_runId == null) { Close(); return; }
            var ok = await _svc.AbandonAsync(_runId, _selectedAbandonReason);
            if (!ok)
            {
                ShowHint("Не удалось прервать — попробуйте ещё раз");
                return;
            }
            Close();
        }

        private async Task OnNextClicked()
        {
            if (_currentNodeKey == null || !_graph.TryGetValue(_currentNodeKey, out var node)) return;

            var answerType = node.AnswerType ?? "none";
            string? value = null;

            if (answerType == "text")
                value = this.FindControl<TextBox>("TextAnswerBox")?.Text?.Trim();
            else if (answerType == "number")
                value = this.FindControl<TextBox>("NumberAnswerBox")?.Text?.Trim();
            // info/none → value stays null

            var comment = this.FindControl<TextBox>("CommentBox")?.Text?.Trim();
            await SubmitAnswerAsync(value, string.IsNullOrEmpty(comment) ? null : comment);
        }

        private async Task OnBackClicked()
        {
            if (_runId == null) return;

            SetWizardBusy(true);
            var nodeKey = await _svc.BackAsync(_runId);
            SetWizardBusy(false);

            if (nodeKey != null)
            {
                if (_stepCounter > 1) _stepCounter--;
                Dispatcher.UIThread.Post(() => RenderNode(nodeKey));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void SetBusy(bool busy)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Show("LoadingLabel", busy);
                Enable("InProgressSection", !busy);
                Enable("AvailableSection", !busy);
            });
        }

        private void SetWizardBusy(bool busy)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Enable("NextBtn", !busy);
                Enable("BackBtn", !busy);
                Enable("AbandonBtn", !busy);
            });
        }

        private void SetTextBlock(string name, string text)
        {
            var el = this.FindControl<TextBlock>(name);
            if (el != null) el.Text = text;
        }

        private void Show(string name, bool visible)
        {
            var el = this.FindControl<Control>(name);
            if (el != null) el.IsVisible = visible;
        }

        private void Enable(string name, bool enabled)
        {
            var el = this.FindControl<Control>(name);
            if (el != null) el.IsEnabled = enabled;
        }
    }
}
