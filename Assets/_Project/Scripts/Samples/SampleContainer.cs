using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey
{
    /// <summary>
    /// The lander's sample container: three XRSocketInteractor slots, a status light per slot and a
    /// hinged lid that closes when the mission succeeds.
    /// </summary>
    public class SampleContainer : MonoBehaviour
    {
        [Header("Slots")]
        [Tooltip("One XRSocketInteractor per sample (each with a SampleSocketFilter).")]
        [SerializeField] private XRSocketInteractor[] sockets = new XRSocketInteractor[0];
        [Tooltip("Optional indicator light per slot (same order as Sockets).")]
        [SerializeField] private Renderer[] slotLights = new Renderer[0];
        [SerializeField] private Color emptyColor = new Color(0.9f, 0.25f, 0.2f);
        [SerializeField] private Color filledColor = new Color(0.3f, 1f, 0.45f);

        [Header("Lid")]
        [Tooltip("Empty GameObject placed on the hinge edge; the lid mesh is its child.")]
        [SerializeField] private Transform lidHinge;
        [SerializeField] private Vector3 lidOpenEuler = new Vector3(110f, 0f, 0f);
        [SerializeField] private Vector3 lidClosedEuler = Vector3.zero;
        [SerializeField] private float lidCloseSeconds = 2.5f;

        [Header("Audio (3D, on the container)")]
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("PLACEHOLDER: metallic 'clunk' when a sample locks in.")]
        [SerializeField] private AudioClip lockClip;
        [Tooltip("PLACEHOLDER: hydraulic hiss / lid closing sound.")]
        [SerializeField] private AudioClip lidCloseClip;

        /// <summary>(secured, capacity)</summary>
        public event Action<int, int> SecuredCountChanged;

        public int Capacity => sockets.Length;
        public int SecuredCount { get; private set; }

        private void Awake()
        {
            if (lidHinge != null) lidHinge.localRotation = Quaternion.Euler(lidOpenEuler);
            UpdateLights();
        }

        private void OnEnable()
        {
            foreach (XRSocketInteractor s in sockets)
            {
                if (s == null) continue;
                s.selectEntered.AddListener(OnSlotEntered);
                s.selectExited.AddListener(OnSlotExited);
            }
        }

        private void OnDisable()
        {
            foreach (XRSocketInteractor s in sockets)
            {
                if (s == null) continue;
                s.selectEntered.RemoveListener(OnSlotEntered);
                s.selectExited.RemoveListener(OnSlotExited);
            }
        }

        private void OnSlotEntered(SelectEnterEventArgs args)
        {
            if (sfxSource != null && lockClip != null) sfxSource.PlayOneShot(lockClip);
            StartCoroutine(RecountNextFrame());
        }

        private void OnSlotExited(SelectExitEventArgs args)
        {
            StartCoroutine(RecountNextFrame());
        }

        // Wait a frame so every socket's "hasSelection" is up to date before counting.
        private IEnumerator RecountNextFrame()
        {
            yield return null;
            int count = 0;
            foreach (XRSocketInteractor s in sockets)
            {
                if (s != null && s.hasSelection) count++;
            }

            if (count == SecuredCount) yield break;
            SecuredCount = count;
            UpdateLights();
            SecuredCountChanged?.Invoke(SecuredCount, Capacity);
        }

        private void UpdateLights()
        {
            for (int i = 0; i < slotLights.Length; i++)
            {
                if (slotLights[i] == null) continue;
                bool filled = i < sockets.Length && sockets[i] != null && sockets[i].hasSelection;
                Material m = slotLights[i].material;
                Color c = filled ? filledColor : emptyColor;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 2f);
                }
            }
        }

        /// <summary>Closes the lid over the samples (called by MissionManager on success).</summary>
        public void CloseLid()
        {
            if (lidHinge == null) return;
            if (sfxSource != null && lidCloseClip != null) sfxSource.PlayOneShot(lidCloseClip);
            StartCoroutine(AnimateLid());
        }

        private IEnumerator AnimateLid()
        {
            Quaternion from = lidHinge.localRotation;
            Quaternion to = Quaternion.Euler(lidClosedEuler);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, lidCloseSeconds);
                lidHinge.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }
    }
}
