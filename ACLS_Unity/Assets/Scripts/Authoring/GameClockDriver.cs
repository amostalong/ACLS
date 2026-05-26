using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ACLS.Sim;

namespace ACLS.Authoring
{
    public sealed class GameClockDriver : MonoBehaviour
    {
        public float SecondsPerDay_Slow = 1.0f;
        public float SecondsPerDay_Normal = 0.3f;
        public float SecondsPerDay_Fast = 0.08f;

        public World World { get; private set; }
        public int Speed { get; private set; } = 2;

        private float accumulator;

        public void Bind(World world)
        {
            World = world;
            accumulator = 0f;
        }

        public void SetSpeed(int speed) => Speed = Mathf.Clamp(speed, 1, 3);

        public void TogglePause()
        {
            if (World == null) return;
            World.Paused = !World.Paused;
        }

        private void Update()
        {
            if (World == null) return;

            // Don't consume hotkeys while the player is typing into an input field —
            // space/1/2/3 should belong to the field, not the clock.
            if (!IsTextInputFocused())
            {
                if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
                if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(1);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(2);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(3);
            }

            if (World.Paused) return;
            if (World.EventQueue.Count > 0) return;  // freeze while a modal is up

            float secondsPerDay = Speed switch
            {
                1 => SecondsPerDay_Slow,
                3 => SecondsPerDay_Fast,
                _ => SecondsPerDay_Normal,
            };

            accumulator += Time.deltaTime;
            int safety = 64;  // bound the inner loop to avoid catastrophe on huge frame stalls
            while (accumulator >= secondsPerDay && !World.Paused && World.EventQueue.Count == 0 && safety-- > 0)
            {
                accumulator -= secondsPerDay;
                World.Tick();
            }
        }

        private static bool IsTextInputFocused()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            if (go == null) return false;
            return go.GetComponent<InputField>() != null;
        }
    }
}
