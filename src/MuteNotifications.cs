using System;

namespace AirPodsMicBridge
{
    // Owned by the audio worker. Observations establish silent baselines; confirmed
    // operations share that state so a delayed native gesture cannot announce twice.
    internal sealed class MuteNotifications
    {
        private string endpointId;
        private bool muted;

        internal bool Observe(string id, bool? value, bool eligible)
        {
            if (!eligible || String.IsNullOrWhiteSpace(id) || !value.HasValue)
            {
                Reset();
                return false;
            }
            if (!String.Equals(endpointId, id, StringComparison.Ordinal))
            {
                endpointId = id;
                muted = value.Value;
                return false;
            }
            bool changed = muted != value.Value;
            muted = value.Value;
            return changed;
        }

        // Call only after a successful, eligible operation has confirmed its result.
        // Unlike passive observation, an operation can announce without a baseline.
        internal bool Confirm(string id, bool value)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                Reset();
                return false;
            }
            bool changed = !String.Equals(endpointId, id, StringComparison.Ordinal) || muted != value;
            endpointId = id;
            muted = value;
            return changed;
        }

        internal void Reset()
        {
            endpointId = null;
            muted = false;
        }
    }
}
