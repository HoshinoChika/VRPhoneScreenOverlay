using System.Numerics;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record OpenVrPlayspaceMotionOptions(
    float FlingStrength = 1,
    float Gravity = 9.8f,
    float Friction = 0,
    bool ResetAllOffsets = false)
{
    public static OpenVrPlayspaceMotionOptions Default { get; } = new();

    public void Validate()
    {
        if (!float.IsFinite(FlingStrength) || FlingStrength is < 0 or > 20 ||
            !float.IsFinite(Gravity) || Gravity is < 0 or > 30 ||
            !float.IsFinite(Friction) || Friction is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(FlingStrength), "Invalid motion settings.");
        }
    }
}

internal sealed record OpenVrPlayspaceMotionConfiguration(bool Enabled, OpenVrPlayspaceMotionOptions Options);

// Adapted from OVR Advanced Settings, GPL-3.0, commit 31df8b4:
// MoveCenterTabController::updateHandDrag / updateGravity / clampVelocity.
// See Resources/OpenVR/OVRAS-NOTICE.txt for attribution and port differences.
internal sealed class OpenVrPlayspaceMotion
{
    public Vector3 Velocity { get; private set; }

    public void Stop() => Velocity = Vector3.Zero;

    public void Capture(Vector3 delta, float seconds, float strength) =>
        Velocity = delta / seconds * strength;

    public Vector3 Step(Vector3 position, float seconds, OpenVrPlayspaceMotionOptions options)
    {
        Vector3 velocity = Velocity;
        if (options.Friction > 0)
        {
            // OVRAS applies linear, time-scaled damping before displacement.
            velocity = MathF.Abs(velocity.X) < 0.0000001f && MathF.Abs(velocity.Y) < 0.0000001f &&
                MathF.Abs(velocity.Z) < 0.0000001f ? Vector3.Zero
                : velocity * Math.Clamp(1 - options.Friction * 0.1f * seconds, 0, 1);
        }
        velocity = new Vector3(float.IsNaN(velocity.X) ? 0 : velocity.X,
            float.IsNaN(velocity.Y) ? 0 : velocity.Y, float.IsNaN(velocity.Z) ? 0 : velocity.Z);
        velocity = Vector3.Clamp(velocity, new Vector3(-50), new Vector3(50));
        // In playspace offsets positive Y moves the player down. The simulated
        // floor is zero relative to the current session basis, not game terrain.
        if (position.Y < 0)
        {
            Vector3 displacement = velocity * seconds;
            if (position.Y + displacement.Y >= 0)
            {
                float fraction = -position.Y / displacement.Y;
                position += displacement * fraction;
                position.Y = 0;
            }
            else
            {
                Velocity = velocity + new Vector3(0, options.Gravity * seconds, 0);
                return position + displacement;
            }
        }
        else if (position.Y > 0)
        {
            position.Y = 0;
        }
        Stop();
        return position;
    }
}
