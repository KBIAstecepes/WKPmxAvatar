using System;

namespace WKPmxAvatar
{
    public static class MotionMath
    {
        public static float Delta(float from, float to)
        {
            float delta = (to - from) % 360;
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            return delta;
        }
        public static float BodyHeading(float current, float look, float travel, float speed, float threshold)
        {
            if (speed > 0.2f)
            {
                float offset = Delta(look, travel);
                // Walking backwards keeps the torso facing the look direction instead of flipping 180 degrees.
                if (Math.Abs(offset) > 110) offset = Delta(look, travel + 180);
                return look + Math.Max(-65, Math.Min(65, offset));
            }
            float yaw = Delta(current, look);
            return current + Math.Sign(yaw) * Math.Max(0, Math.Abs(yaw) - threshold);
        }
    }
}
