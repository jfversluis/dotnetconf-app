using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class FavoriteButton : Grid
{
    public static readonly BindableProperty ButtonAutomationIdProperty = BindableProperty.Create(
        nameof(ButtonAutomationId), typeof(string), typeof(FavoriteButton), "");

    private readonly FavoriteHalo halo = new();
    private SessionViewModel? session;
    private bool loaded;

    public string ButtonAutomationId
    {
        get => (string)GetValue(ButtonAutomationIdProperty);
        set => SetValue(ButtonAutomationIdProperty, value);
    }

    public FavoriteButton()
    {
        InitializeComponent();
        Halo.Drawable = halo;
        Loaded += (_, _) =>
        {
            loaded = true;
            ObserveSession();
        };
        Unloaded += (_, _) =>
        {
            loaded = false;
            ObserveSession();
        };
        BindingContextChanged += (_, _) => ObserveSession();
    }

    private void ObserveSession()
    {
        if (session is not null)
            session.PropertyChanged -= OnSessionChanged;
        ResetCelebration();
        session = loaded ? BindingContext as SessionViewModel : null;
        if (session is not null)
            session.PropertyChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionViewModel.IsSaved))
            return;

        ResetCelebration();
        if (AskGlow.ReduceMotion)
            return;

        var animation = new Animation();
        if (session?.IsSaved == true)
        {
            animation.Add(0, .16, new Animation(value => Heart.Scale = value, 1, .78, Easing.CubicIn));
            animation.Add(.16, .44, new Animation(value => Heart.Scale = value, .78, 1.32, Easing.CubicOut));
            animation.Add(.44, .70, new Animation(value => Heart.Scale = value, 1.32, .94, Easing.CubicInOut));
            animation.Add(.70, 1, new Animation(value => Heart.Scale = value, .94, 1, Easing.CubicOut));
            animation.Add(.10, .32, new Animation(value => Heart.Rotation = value, 0, -12, Easing.CubicOut));
            animation.Add(.32, .60, new Animation(value => Heart.Rotation = value, -12, 6, Easing.CubicInOut));
            animation.Add(.60, .90, new Animation(value => Heart.Rotation = value, 6, 0, Easing.CubicOut));
            animation.Add(.16, 1, new Animation(value =>
            {
                halo.Progress = value;
                Halo.Invalidate();
            }));
        }
        else
        {
            animation.Add(0, .4, new Animation(value => Heart.Scale = value, 1, .88, Easing.CubicOut));
            animation.Add(.4, 1, new Animation(value => Heart.Scale = value, .88, 1, Easing.CubicOut));
        }

        animation.Commit(this, "FavoriteCelebration", length: session?.IsSaved == true ? 620u : 180u,
            finished: (_, _) => ResetVisuals());
    }

    private void ResetCelebration()
    {
        this.AbortAnimation("FavoriteCelebration");
        ResetVisuals();
    }

    private void ResetVisuals()
    {
        Heart.Scale = 1;
        Heart.Rotation = 0;
        halo.Progress = 0;
        Halo.Invalidate();
    }

    private sealed class FavoriteHalo : IDrawable
    {
        public double Progress { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Progress <= 0 || Progress >= 1 || AskGlow.ReduceMotion)
                return;

            var app = Application.Current;
            if (app is null)
                return;
            var color = (Color)app.Resources[app.RequestedTheme == AppTheme.Dark ? "Lavender" : "Purple"];
            var progress = (float)Progress;
            var eased = 1 - MathF.Pow(1 - progress, 3);
            var alpha = (1 - progress) * (1 - progress);
            var x = dirtyRect.Center.X;
            var y = dirtyRect.Center.Y;
            canvas.FillColor = color.WithAlpha(.12f * alpha);
            canvas.FillCircle(x, y, 8 + 14 * eased);
            canvas.StrokeColor = color.WithAlpha(.65f * alpha);
            canvas.StrokeSize = 1.5f * (1 - progress);
            canvas.DrawCircle(x, y, 10 + 12 * eased);
            canvas.FillColor = color.WithAlpha(alpha);
            for (var i = 0; i < 6; i++)
            {
                var angle = i * MathF.PI / 3 - MathF.PI / 2;
                var radius = 12 + 10 * eased;
                canvas.FillCircle(x + MathF.Cos(angle) * radius, y + MathF.Sin(angle) * radius,
                    2 * (1 - progress));
            }
        }
    }
}
