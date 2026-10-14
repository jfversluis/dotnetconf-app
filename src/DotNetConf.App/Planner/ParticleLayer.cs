namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

/// <summary>
/// Short bursts of hearts and sparks where a session is saved. Nothing runs while it is idle.
/// </summary>
internal sealed class ParticleLayer : Control
{
    private const double Gravity = 520;
    private const double RingLife = 0.55;
    private static readonly CubicEaseOut EaseOut = new();

    private readonly List<(Point Position, Vector Velocity, double Size, double Life, double Spin, Color Color, bool IsHeart, double Age, double Angle)> particles = [];
    private readonly List<(Point Center, Color Color, double Age)> rings = [];
    private TimeSpan? lastFrame;
    private bool frameRequested;

    public ParticleLayer() => IsHitTestVisible = false;

    public void Burst(Point origin, IReadOnlyList<Color> colors)
    {
        rings.Add((origin, colors[0], 0));
        for (var i = 0; i < 18; i++)
        {
            // Mostly upwards, like confetti thrown from the heart.
            var angle = -Math.PI / 2 + (Random.Shared.NextDouble() - 0.5) * Math.PI * 1.5;
            var speed = 140 + Random.Shared.NextDouble() * 200;
            particles.Add((
                Position: origin,
                Velocity: new Vector(Math.Cos(angle), Math.Sin(angle)) * speed,
                Size: 5 + Random.Shared.NextDouble() * 8,
                Life: 0.7 + Random.Shared.NextDouble() * 0.5,
                Spin: (Random.Shared.NextDouble() - 0.5) * 6,
                Color: colors[i % colors.Count],
                IsHeart: i % 3 == 0,
                Age: 0,
                Angle: 0));
        }
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (frameRequested)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            particles.Clear();
            rings.Clear();
            return;
        }
        frameRequested = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        frameRequested = false;
        var elapsed = lastFrame is { } last ? Math.Min((time - last).TotalSeconds, 0.05) : 1.0 / 60;
        lastFrame = time;
        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            particle.Age += elapsed;
            particle.Velocity = (particle.Velocity + new Vector(0, Gravity * elapsed)) * (1 - 1.4 * elapsed);
            particle.Position += particle.Velocity * elapsed;
            particle.Angle += particle.Spin * elapsed;
            particles[i] = particle;
        }
        particles.RemoveAll(particle => particle.Age >= particle.Life);
        for (var i = 0; i < rings.Count; i++)
            rings[i] = rings[i] with { Age = rings[i].Age + elapsed };
        rings.RemoveAll(ring => ring.Age >= RingLife);
        InvalidateVisual();
        if (particles.Count > 0 || rings.Count > 0)
            RequestFrame();
        else
            lastFrame = null;
    }

    public override void Render(DrawingContext context)
    {
        foreach (var ring in rings)
        {
            var progress = ring.Age / RingLife;
            var radius = 10 + 36 * EaseOut.Ease(progress);
            var pen = new Pen(new SolidColorBrush(ring.Color, 0.55 * (1 - progress)), 2.5 * (1 - progress) + 0.5);
            context.DrawEllipse(null, pen, ring.Center, radius, radius);
        }
        foreach (var particle in particles)
        {
            var progress = particle.Age / particle.Life;
            var brush = new SolidColorBrush(particle.Color, 1 - progress * progress);
            var size = particle.Size * (1 - 0.35 * progress);
            if (particle.IsHeart)
            {
                var transform = Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(size / 16, size / 16)
                    * Matrix.CreateRotation(particle.Angle) * Matrix.CreateTranslation(particle.Position.X, particle.Position.Y);
                using (context.PushTransform(transform))
                    context.DrawGeometry(brush, null, PlannerTheme.HeartIcon);
            }
            else
            {
                context.DrawEllipse(brush, null, particle.Position, size / 2, size / 2);
            }
        }
    }
}
