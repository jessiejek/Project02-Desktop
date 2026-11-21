//JJQR - AI (Nov 21, 2026) - desktop page frame: the single child fills the window up to MaxContentWidth and is centred; on narrower windows it simply fills.
using Microsoft.Maui.Layouts;

namespace ClinicNode.App.Controls;

public class MaxWidthLayout : Layout
{
    public static readonly BindableProperty MaxContentWidthProperty =
        BindableProperty.Create(nameof(MaxContentWidth), typeof(double), typeof(MaxWidthLayout), 1320d,
            propertyChanged: (b, _, _) => ((MaxWidthLayout)b).InvalidateMeasure());

    public double MaxContentWidth
    {
        get => (double)GetValue(MaxContentWidthProperty);
        set => SetValue(MaxContentWidthProperty, value);
    }

    protected override ILayoutManager CreateLayoutManager() => new MaxWidthLayoutManager(this);

    sealed class MaxWidthLayoutManager(MaxWidthLayout layout) : ILayoutManager
    {
        double ChildWidth(double available) =>
            double.IsInfinity(available) ? layout.MaxContentWidth : Math.Min(available, layout.MaxContentWidth);

        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var w = ChildWidth(widthConstraint);
            var h = 0d;
            foreach (var child in layout.Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var s = child.Measure(w, heightConstraint);
                h = Math.Max(h, s.Height);
            }
            return new Size(double.IsInfinity(widthConstraint) ? w : widthConstraint, double.IsInfinity(heightConstraint) ? h : heightConstraint);
        }

        public Size ArrangeChildren(Rect bounds)
        {
            var w = Math.Min(bounds.Width, layout.MaxContentWidth);
            var x = bounds.X + (bounds.Width - w) / 2;
            foreach (var child in layout.Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Arrange(new Rect(x, bounds.Y, w, bounds.Height));
            }
            return bounds.Size;
        }
    }
}
//JJQR - END
