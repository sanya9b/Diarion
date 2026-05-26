using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class ProgressRing : GraphicsView
{
    public static readonly BindableProperty ProgressProperty = 
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressRing), 0.0, propertyChanged: OnProgressChanged);
        
    public static readonly BindableProperty RingColorProperty = 
        BindableProperty.Create(nameof(RingColor), typeof(Color), typeof(ProgressRing), Colors.Transparent, propertyChanged: OnProgressChanged);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    private static void OnProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ProgressRing ring)
        {
            ring.Invalidate();
        }
    }

    public ProgressRing()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new RingDrawable(this);
    }

    private class RingDrawable : IDrawable
    {
        private readonly ProgressRing _ring;
        public RingDrawable(ProgressRing ring) => _ring = ring;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (_ring.Progress <= 0 || _ring.RingColor == Colors.Transparent)
                return;

            float strokeWidth = 3f;
            float margin = strokeWidth / 2f;
            
            var rect = new RectF(margin, margin, dirtyRect.Width - strokeWidth, dirtyRect.Height - strokeWidth);
            
            canvas.StrokeColor = _ring.RingColor;
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Round;

            if (_ring.Progress >= 1.0)
            {
                // DrawArc може не намалювати нічого при повному 360-градусному колі через збіг кутів в Android Canvas
                canvas.DrawEllipse(rect);
            }
            else
            {
                // Початок (12 годин) в градусах MAUI = 90
                float startAngle = 90f;
                
                // Кінцевий кут: ми малюємо ЗА годинниковою стрілкою (negative sweep).
                // Віднімаємо відсоток у градусах.
                float endAngle = startAngle - (float)(_ring.Progress * 360.0);
                
                // Вказуємо clockwise = true
                canvas.DrawArc(rect, startAngle, endAngle, true, false);
            }
        }
    }
}
