namespace SmartRoom
{
    /// <summary>Friendly names and effects for the sensor keys used in .../faults and fault events.</summary>
    public static class FaultInfo
    {
        public const string Lm35 = "lm35";
        public const string Light = "light";
        public const string Pir = "pir";
        public const string Hcsr04 = "hcsr04";
        public const string Rotation = "rotation";
        public const string Sen0291 = "sen0291";

        /// <summary>Full name for the alert banner, e.g. "Temperature sensor (LM35)".</summary>
        public static string Name(string key)
        {
            switch (key)
            {
                case Lm35: return "Temperature sensor (LM35)";
                case Light: return "Light sensor";
                case Pir: return "Motion sensor (PIR)";
                case Hcsr04: return "Door sensor (ultrasonic)";
                case Rotation: return "Temperature knob";
                case Sen0291: return "Wattmeter";
                default: return key;
            }
        }

        /// <summary>Short name for the event log, e.g. "Temperature sensor".</summary>
        public static string ShortName(string key)
        {
            switch (key)
            {
                case Lm35: return "Temperature sensor";
                case Light: return "Light sensor";
                case Pir: return "Motion sensor";
                case Hcsr04: return "Door sensor";
                case Rotation: return "Temperature knob";
                case Sen0291: return "Wattmeter";
                default: return key;
            }
        }

        /// <summary>What the board does about the fault, in plain words.</summary>
        public static string Effect(string key)
        {
            switch (key)
            {
                case Lm35: return "The fan (AC) was switched off. You can still switch it with the override.";
                case Light: return "The lights were switched off. You can still switch them with the override.";
                case Pir: return "Occupancy unknown, so lights and AC were switched off. Overrides stay on until you switch them off.";
                case Hcsr04: return "Door state unknown; the AC won't pause for the door.";
                case Rotation: return "Set the target from the app instead.";
                case Sen0291: return "Live power unavailable; energy is estimated.";
                default: return "";
            }
        }
    }
}
