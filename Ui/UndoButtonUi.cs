using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Undo;

namespace UndoModMS.Ui;

internal static class UndoButtonUi
{
    private static Button? _button;

    private static Vector2 _dragGrabOffset;
    private static Vector2 _dragStartPos;
    private const float DragMinPixels = 3f;

    private static readonly Color BgNormal = Color.FromHtml("#1F140B");
    private static readonly Color BgHover = Color.FromHtml("#34220F");
    private static readonly Color BgPressed = Color.FromHtml("#0F0905");
    private static readonly Color BgDisabled = Color.FromHtml("#1A130C");
    private static readonly Color BorderGold = Color.FromHtml("#C8A45A");
    private static readonly Color BorderGoldHi = Color.FromHtml("#F2D080");
    private static readonly Color BorderDim = Color.FromHtml("#5A4828");
    private static readonly Color TextParchment = Color.FromHtml("#F2E2B8");
    private static readonly Color TextHi = Color.FromHtml("#FFF6D8");
    private static readonly Color TextDim = Color.FromHtml("#7A6A48");

    public static void Install()
    {
        if (_button != null && GodotObject.IsInstanceValid(_button)) return;

        var anchor = FindEnergyAnchor();
        if (anchor == null)
        {
            UndoLogger.Warn("[Ui] energy anchor not found — undo button skipped");
            return;
        }
        var parent = anchor.GetParent();
        if (parent == null) return;

        var btn = new Button
        {
            Text = "↶ Z",
            TooltipText = LocalizedTooltip(),
            CustomMinimumSize = new Vector2(64, 64),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyTheme(btn);
        btn.Pressed += OnPressed;
        btn.GuiInput += OnButtonGuiInput;

        Vector2 anchorPos = ReadPosition(anchor);
        Vector2 anchorSize = ReadSize(anchor);
        Vector2 defaultPos = anchorPos + new Vector2(
            -btn.CustomMinimumSize.X - 20,
            -anchorSize.Y * 0.35f - 12 + btn.CustomMinimumSize.Y * 0.5f);

        var savedX = ModSettings.Data.IconX;
        var savedY = ModSettings.Data.IconY;
        btn.Position = (savedX is float sx && savedY is float sy)
            ? new Vector2(sx, sy)
            : defaultPos;

        parent.AddChild(btn);
        _button = btn;

        var timer = new Godot.Timer
        {
            WaitTime = 0.1,
            Autostart = true,
            OneShot = false,
            ProcessCallback = Godot.Timer.TimerProcessCallback.Idle,
        };
        timer.Timeout += OnPollTick;
        btn.AddChild(timer);

        UndoLogger.Info($"[Ui] undo button installed near {anchor.GetType().Name} at {btn.Position}");
    }

    private static void OnPollTick()
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;

        try { Snapshot.CombatSnapshot.RefreshIdleCacheFromLiveCreatures(); }
        catch { }

        try { ReconcileReviveCreatureHpBars(); } catch { }

        bool canUndo = false;
        try { canUndo = UndoController.CanRestoreNowPublic(); } catch { }

        if (_button.Disabled != !canUndo)
        {
            _button.Disabled = !canUndo;
            if (!_button.Disabled) _button.Modulate = Colors.White;
        }
    }

    private static void ReconcileReviveCreatureHpBars()
    {
        var cm = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
        if (cm == null) return;
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (room == null) return;
        if (Snapshot.ReflectionCache.CombatManagerStateField?.GetValue(cm) is not MegaCrit.Sts2.Core.Combat.CombatState cs) return;

        foreach (var c in cs.Creatures)
        {
            if (c == null) continue;
            var nc = room.GetCreatureNode(c);
            if (nc == null) continue;
            if (Patches.AnimDiePatch.FindReviveLikePower(nc) == null) continue;

            bool wantVisible = !c.IsDead;
            foreach (var n in Snapshot.SnapshotRestorer.WalkNodeTree(nc))
            {
                if (n is not NCreatureStateDisplay sd) continue;
                try
                {
                    var mod = sd.Modulate;
                    if (wantVisible)
                    {
                        if (!sd.Visible || mod.A <= 0.01f)
                        {
                            sd.Visible = true;
                            mod.A = 1f;
                            sd.Modulate = mod;
                        }
                    }
                    else
                    {
                        if (sd.Visible && mod.A >= 0.999f)
                        {
                            sd.Visible = false;
                            mod.A = 0f;
                            sd.Modulate = mod;
                        }
                    }
                }
                catch { }
            }
        }
    }

    public static void Uninstall()
    {
        if (_button == null) return;
        try
        {
            if (GodotObject.IsInstanceValid(_button))
            {
                _button.Pressed -= OnPressed;
                _button.GuiInput -= OnButtonGuiInput;
                _button.QueueFree();
            }
        }
        catch { }
        _button = null;
    }

    private static void OnPressed()
    {
        UndoController.Undo();
    }

    private static void OnButtonGuiInput(InputEvent ev)
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;

        if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed)
            {
                _dragStartPos = _button.Position;
                _dragGrabOffset = mb.Position;
                _button.AcceptEvent();
            }
            else
            {
                var moved = (_button.Position - _dragStartPos).Length();
                if (moved >= DragMinPixels)
                {
                    ModSettings.SetIconPosition(_button.Position.X, _button.Position.Y);
                    UndoLogger.Info($"[Ui] icon repositioned to {_button.Position} — saved");
                }
                _button.AcceptEvent();
            }
            return;
        }

        if (ev is InputEventMouseMotion mm && _dragGrabOffset != Vector2.Zero
            && Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _button.Position += mm.Position - _dragGrabOffset;
            _button.AcceptEvent();
        }
    }

    private static void ApplyTheme(Button btn)
    {
        btn.AddThemeStyleboxOverride("normal", BuildPanel(BgNormal, BorderGold, 2));
        btn.AddThemeStyleboxOverride("hover", BuildPanel(BgHover, BorderGoldHi, 3));
        btn.AddThemeStyleboxOverride("pressed", BuildPanel(BgPressed, BorderGoldHi, 3, pressed: true));
        btn.AddThemeStyleboxOverride("disabled", BuildPanel(BgDisabled, BorderDim, 2));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        btn.AddThemeColorOverride("font_color", TextParchment);
        btn.AddThemeColorOverride("font_hover_color", TextHi);
        btn.AddThemeColorOverride("font_pressed_color", TextParchment);
        btn.AddThemeColorOverride("font_disabled_color", TextDim);
        btn.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        btn.AddThemeConstantOverride("outline_size", 4);
        btn.AddThemeFontSizeOverride("font_size", 22);
    }

    private static StyleBoxFlat BuildPanel(Color bg, Color border, int borderWidth, bool pressed = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            ShadowColor = new Color(0, 0, 0, pressed ? 0.35f : 0.7f),
            ShadowSize = pressed ? 2 : 6,
            ShadowOffset = new Vector2(0, pressed ? 1 : 3),
            AntiAliasing = true,
        };
        sb.SetBorderWidthAll(borderWidth);
        return sb;
    }

    private static Node? FindEnergyAnchor()
    {
        var ngame = NGame.Instance;
        if (ngame == null) return null;

        NCombatUi? ui = null;
        foreach (var n in Snapshot.SnapshotRestorer.WalkNodeTree(ngame))
        {
            if (n is NCombatUi found) { ui = found; break; }
        }

        Node? Pick(Node root)
        {
            foreach (var n in Snapshot.SnapshotRestorer.WalkNodeTree(root))
            {
                if (n.GetType().Name == "NEnergyCounter") return n;
            }
            return null;
        }

        if (ui != null)
        {
            var byUi = Pick(ui);
            if (byUi != null) return byUi;
        }
        return Pick(ngame);
    }

    private static Vector2 ReadPosition(Node n)
    {
        var prop = AccessTools.Property(n.GetType(), "Position");
        if (prop?.GetValue(n) is Vector2 v) return v;
        return Vector2.Zero;
    }

    private static Vector2 ReadSize(Node n)
    {
        var prop = AccessTools.Property(n.GetType(), "Size");
        if (prop?.GetValue(n) is Vector2 v) return v;
        var customMin = AccessTools.Property(n.GetType(), "CustomMinimumSize");
        if (customMin?.GetValue(n) is Vector2 c) return c;
        return new Vector2(64, 64);
    }

    private static string LocalizedTooltip()
    {
        var locale = TranslationServer.GetLocale() ?? string.Empty;
        if (locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return "되돌리기 (Z) / 턴 되돌리기 (Shift+Z)\n드래그로 위치 이동";
        return "Undo (Z) / Undo Turn (Shift+Z)\nDrag to move";
    }
}