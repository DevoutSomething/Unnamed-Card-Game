using UnityEngine;

namespace Game.Client.View
{
    /// <summary>
    /// Shakes its RectTransform for a short burst when Shake() is called, then
    /// settles back to where it started. Put it on a wrapper whose resting
    /// anchoredPosition is its home (e.g. a centered health readout), so combat
    /// damage can jolt the display without disturbing layout around it.
    /// </summary>
    public class UiShaker : MonoBehaviour
    {
        const float Duration = 0.35f;
        const float Magnitude = 9f;

        RectTransform _rect;
        Vector2 _home;
        bool _homeSet;
        float _remaining;

        void Awake()
        {
            _rect = (RectTransform)transform;
            CaptureHome();
        }

        /// <summary>Re-reads the resting position — call after (re)positioning the wrapper.</summary>
        public void CaptureHome()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            _homeSet = true;
        }

        public void Shake()
        {
            if (!_homeSet) CaptureHome();
            _remaining = Duration;
        }

        void Update()
        {
            if (_remaining <= 0f) return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _rect.anchoredPosition = _home;
                return;
            }

            float damper = _remaining / Duration;   // fades the jolt out over the burst
            _rect.anchoredPosition = _home +
                new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (Magnitude * damper);
        }
    }
}
