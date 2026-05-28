using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class ProgressBorder : GraphicsView
{
    public static readonly BindableProperty ProgressProperty = 
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressBorder), 0.0, propertyChanged: OnProgressChanged);
        
    public static readonly BindableProperty StrokeColorProperty = 
        BindableProperty.Create(nameof(StrokeColor), typeof(Color), typeof(ProgressBorder), Colors.Transparent, propertyChanged: OnProgressChanged);

    public static readonly BindableProperty CornerRadiusProperty = 
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(ProgressBorder), 8.0, propertyChanged: OnProgressChanged);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ProgressBorder border)
        {
            border.Invalidate();
        }
    }

    public ProgressBorder()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new BorderDrawable(this);
    }

    private class BorderDrawable : IDrawable
    {
        private readonly ProgressBorder _border;
        public BorderDrawable(ProgressBorder border) => _border = border;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (_border.Progress <= 0 || _border.StrokeColor == Colors.Transparent)
                return;

            float strokeWidth = 3f;
            float margin = strokeWidth / 2f;
            float r = Math.Max(0f, (float)_border.CornerRadius - margin);
            
            float w = dirtyRect.Width - strokeWidth;
            float h = dirtyRect.Height - strokeWidth;
            
            canvas.StrokeColor = _border.StrokeColor;
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            if (_border.Progress >= 1.0)
            {
                canvas.DrawRoundedRectangle(margin, margin, w, h, r);
                return;
            }

            PathF path = new PathF();
            
            float currentLen = 0;
            float targetLength = (float)((2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * Math.PI * r) * _border.Progress);

            float lastX = margin + w / 2;
            float lastY = margin;
            path.MoveTo(lastX, lastY);

            void AddLine(float x, float y) {
                if (currentLen >= targetLength) return;
                float dx = x - lastX;
                float dy = y - lastY;
                float len = (float)Math.Sqrt(dx*dx + dy*dy);
                
                if (len < 0.0001f) return; // Prevent division by zero and micro-segments

                if (currentLen + len >= targetLength) {
                    float ratio = (targetLength - currentLen) / len;
                    lastX = lastX + dx * ratio;
                    lastY = lastY + dy * ratio;
                    path.LineTo(lastX, lastY);
                    currentLen = targetLength;
                } else {
                    lastX = x;
                    lastY = y;
                    path.LineTo(lastX, lastY);
                    currentLen += len;
                }
            }

            void AddCorner(float cx, float cy, float startAngleDeg, float endAngleDeg) {
                if (currentLen >= targetLength) return;
                
                int steps = 15; // 15 line segments per quarter circle is visually perfectly smooth for small radii
                float sweepAngle = endAngleDeg - startAngleDeg;
                float arcLen = (float)(r * Math.Abs(sweepAngle) * Math.PI / 180f);
                
                float angleStep = sweepAngle / steps;
                
                for (int i = 1; i <= steps; i++) {
                    float currentAngle = startAngleDeg + angleStep * i;
                    
                    // Convert to radians (0 is Right, 90 is Down/Bottom in MAUI)
                    float rad = currentAngle * (float)Math.PI / 180f;
                    float px = cx + r * (float)Math.Cos(rad);
                    float py = cy + r * (float)Math.Sin(rad);
                    
                    AddLine(px, py);
                    if (currentLen >= targetLength) return;
                }
            }

            // MAUI Coordinates: 
            // 0 degrees = Right (1, 0)
            // 90 degrees = Bottom (0, 1)
            // 180 degrees = Left (-1, 0)
            // 270 degrees = Top (0, -1)
            // We want to draw clockwise starting from Top Center.
            // Top Center -> Top Right -> Bottom Right -> Bottom Left -> Top Left -> Top Center.
            
            // 1. Line to top-right corner start
            AddLine(margin + w - r, margin); 
            // 2. Top-right corner (from Top: 270 to Right: 360)
            AddCorner(margin + w - r, margin + r, 270, 360); 
            
            // 3. Line to bottom-right corner start
            AddLine(margin + w, margin + h - r); 
            // 4. Bottom-right corner (from Right: 0 to Bottom: 90)
            AddCorner(margin + w - r, margin + h - r, 0, 90); 
            
            // 5. Line to bottom-left corner start
            AddLine(margin + r, margin + h); 
            // 6. Bottom-left corner (from Bottom: 90 to Left: 180)
            AddCorner(margin + r, margin + h - r, 90, 180); 
            
            // 7. Line to top-left corner start
            AddLine(margin, margin + r); 
            // 8. Top-left corner (from Left: 180 to Top: 270)
            AddCorner(margin + r, margin + r, 180, 270); 
            
            // 9. Line back to top center (close loop)
            AddLine(margin + w / 2, margin); 

            canvas.DrawPath(path);
        }
    }
}