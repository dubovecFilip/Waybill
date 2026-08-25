using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Waybill;

/// <summary>
/// Somewhere to sign.
///
/// Drawn on paper rather than on the dark window it opens from, with the same stock,
/// the same ink and the same ruled line the sheet has, because what is being decided
/// here is how it will look there. A pad in the app's own colours would be a picture
/// of a different document.
/// </summary>
public sealed class SignaturePad : Form {
    private readonly List<List<PointF>> _strokes = new();
    private List<PointF>? _drawing;
    private readonly Panel _pad = new();

    private static readonly Color Paper = Color.FromArgb(237, 230, 212);
    private static readonly Color Rule = Color.FromArgb(195, 185, 159);
    private static readonly Color Ink = Color.FromArgb(35, 64, 107);

    /// <summary>The strokes, in the form <see cref="Signature"/> stores them. Not a
    /// property: a public one on a Form is something the designer wants to serialize,
    /// and this is an answer the dialog gives back, not a setting on it.</summary>
    public string Written = "";

    public SignaturePad(string? existing, Color surface, Color raised, Color line, Color text, Color muted) {
        Text = Strings.T("sign.title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 330);
        BackColor = surface;
        ForeColor = text;

        _strokes.AddRange(Signature.Read(existing));

        _pad.SetBounds(20, 46, 520, 200);
        _pad.BackColor = Paper;
        _pad.Cursor = Cursors.Cross;
        _pad.Paint += PaintPad;
        _pad.MouseDown += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            _drawing = new List<PointF> { e.Location };
            _strokes.Add(_drawing);
        };
        _pad.MouseMove += (_, e) => {
            if (_drawing is null) return;
            var last = _drawing[^1];
            // Points closer together than this say nothing the line does not already
            // say, and a stroke of them would be a settings file full of noise.
            if (Math.Abs(e.X - last.X) < 2 && Math.Abs(e.Y - last.Y) < 2) return;
            _drawing.Add(e.Location);
            _pad.Invalidate();
        };
        _pad.MouseUp += (_, _) => {
            if (_drawing is { Count: <= 1 }) _strokes.Remove(_drawing);
            _drawing = null;
            _pad.Invalidate();
        };
        Controls.Add(_pad);

        Controls.Add(new Label {
            Location = new Point(20, 18), Size = new Size(520, 22),
            Text = Strings.T("sign.hint"), ForeColor = muted,
        });

        Button Choice(string caption, int right, DialogResult result, Action? click = null) {
            var b = new Button {
                Text = caption, Width = 120, Height = 30,
                FlatStyle = FlatStyle.Flat, BackColor = raised, ForeColor = text,
                DialogResult = result, Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = line;
            b.SetBounds(right - b.Width, 262, b.Width, b.Height);
            if (click is not null) b.Click += (_, _) => click();
            Controls.Add(b);
            return b;
        }

        Choice(Strings.T("sign.clear"), 160, DialogResult.None, () => {
            _strokes.Clear();
            _drawing = null;
            _pad.Invalidate();
        });
        var cancel = Choice(Strings.T("button.cancel"), 410, DialogResult.Cancel);
        var save = Choice(Strings.T("sign.save"), 540, DialogResult.OK, () => Written = Normalised());
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The strokes as fractions of the width they were drawn in.</summary>
    private string Normalised() {
        var w = Math.Max(1, _pad.ClientSize.Width);
        return Signature.Write(_strokes.Select(s =>
            (IReadOnlyList<PointF>)s.Select(p => new PointF(p.X / w, p.Y / w)).ToList()));
    }

    private void PaintPad(object? sender, PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The line to sign above, where the sheet puts it.
        using (var rule = new Pen(Rule, 1.4f)) {
            var y = _pad.ClientSize.Height - 44;
            g.DrawLine(rule, 30, y, _pad.ClientSize.Width - 30, y);
        }

        using var pen = new Pen(Ink, 2.2f) {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        foreach (var stroke in _strokes) {
            if (stroke.Count > 1) g.DrawLines(pen, stroke.ToArray());
        }
    }
}
