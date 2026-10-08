using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey.EditorTools
{
    /// <summary>
    /// Menu: Lunar Survey > Apply Play-test Fixes
    ///
    /// Non-destructive and safe to run any number of times: it finds the objects the scene builder made and
    /// UPDATES them in place (it never deletes [Mission] / [Environment]), so hand edits survive. The scene
    /// builder also calls it at the end of every build step, so a fresh "Build Everything" includes the fixes.
    ///
    /// Fixes from the play-test (see PLAYTEST_FIXES.md in the project root):
    ///  Scanner   - bigger flip-up screen (was half buried in the body), readout mirrored on the wrist,
    ///              velocity-tracked so it no longer clips through the table/floor, belt holster on the player,
    ///              click-to-toggle grab without a headset.
    ///  Player    - terrain following + survey-zone clamp, invisible wall ring with marker stakes.
    ///  Sky       - visible Sun disc locked to the light, textured + sun-lit Earth (real phases), both "at infinity".
    ///  Storm     - radiation storm effects (dosimeter, static, cosmic-ray flashes, levitating dust, visor tint).
    ///  Movement  - Apollo-style hop gait in Lunar mode, footsteps.
    ///  Audio     - assigns the generated SFX (Tools/generate_sfx.py) and any recorded Houston lines.
    ///  Rendering - shadow distance raised so rocks and the lander cast long lunar shadows.
    /// </summary>
    public static class LunarPlaytestFixes
    {
        private const string GenRoot = "Assets/_Project/Generated";
        private const string SfxFolder = "Assets/_Project/Audio/SFX";
        private const string CommsFolder = "Assets/_Project/Audio/Comms";
        private const string EarthTexturePath = "Assets/_Project/Textures/Earth/EarthBlueMarble.jpg";

        private static readonly Vector3 PlayAreaCentre = new Vector3(-1.5f, 0f, 6f);
        private const float WallRadius = 32f;      // inner face of the invisible wall ring
        private const float ClampRadius = 31.5f;   // PlayerGrounding keeps the head inside this
        private static readonly Vector3 EarthDirection = new Vector3(0.35f, 0.45f, 1f);

        [MenuItem("Lunar Survey/Apply Play-test Fixes (safe - keeps your edits)", priority = 10)]
        public static void Menu()
        {
            ApplyAll();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[LunarPlaytestFixes] Done. Save the scene (Ctrl/Cmd+S) and press Play.");
        }

        public static void ApplyAll()
        {
            var log = new List<string>();
            MissionManager mission = FindInScene<MissionManager>();
            GameObject managers = mission != null ? mission.gameObject : null;
            XROrigin origin = FindInScene<XROrigin>();
            Camera cam = origin != null ? origin.Camera : Camera.main;

            TMP_Text wristScan = FixWristDisplay(origin, log);
            GeoScanner scanner = FixScanner(wristScan, log);
            FixHolster(origin, scanner, log);

            if (managers != null)
            {
                MissionComms comms = FindInScene<MissionComms>();
                LocomotionModeController loco = GetOrAdd<LocomotionModeController>(managers);
                GetOrAdd<GrabStyleController>(managers);

                PlayerGrounding grounding = GetOrAdd<PlayerGrounding>(managers);
                Set(grounding, "comms", comms);
                SetVector(grounding, "centre", PlayAreaCentre);
                SetFloat(grounding, "radius", ClampRadius);

                LunarGait gait = GetOrAdd<LunarGait>(managers);
                Set(gait, "locomotion", loco);
                AudioSource steps = ChildAudio(managers.transform, "Footsteps (2D, through the suit)", loop: false, volume: 1f);
                Set(gait, "footstepSource", steps);
                SetList(gait, "footstepClips", Sfx("footstep_1"), Sfx("footstep_2"), Sfx("footstep_3"));
                log.Add("Player: terrain following, survey-zone clamp, lunar hop gait, grab style");

                Transform sunDisc = FixSky(log);
                FixStorm(managers, cam, comms, sunDisc, log);
                FixAudio(managers, comms, scanner, log);
            }
            else
            {
                log.Add("SKIPPED mission fixes: no MissionManager in the scene (run Build Everything first)");
            }

            FixBoundary(log);
            FixShadows(log);

            Debug.Log("[LunarPlaytestFixes]\n - " + string.Join("\n - ", log));
        }

        // ============================================================================ SCANNER

        private static GeoScanner FixScanner(TMP_Text wristScan, List<string> log)
        {
            GeoScanner scanner = FindInScene<GeoScanner>();
            if (scanner == null) { log.Add("SKIPPED scanner: none in scene"); return null; }

            // Velocity tracking = the held scanner is a real physics body, so it stops at the table, crates
            // and the ground instead of passing through them (Instantaneous ignores collisions while held).
            var grab = scanner.GetComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false; // it goes back to the belt anyway
            EditorUtility.SetDirty(grab);
            var rb = scanner.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            // Screen: was 9 x 5 cm, fixed at 55 deg and half buried inside the body. Now a 13 x 8.5 cm display on
            // a hinge at the back of the scanner that tilts itself toward the player's eyes (ScannerScreenTilt).
            Transform screen = FindChild(scanner.transform, "Screen");
            if (screen is RectTransform rt)
            {
                Transform hinge = FindChild(scanner.transform, "Screen Hinge");
                if (hinge == null)
                {
                    hinge = new GameObject("Screen Hinge").transform;
                    hinge.SetParent(scanner.transform, false);
                }
                hinge.localPosition = new Vector3(0f, 0.028f, -0.10f); // back top edge of the body
                hinge.localRotation = Quaternion.Euler(35f, 0f, 0f);
                GetOrAdd<ScannerScreenTilt>(hinge.gameObject);

                const float w = 320, hgt = 210, mpp = 0.0004f;
                rt.SetParent(hinge, false);
                rt.sizeDelta = new Vector2(w, hgt);
                rt.localScale = Vector3.one * mpp;
                rt.localRotation = Quaternion.identity;
                rt.localPosition = new Vector3(0f, hgt * mpp * 0.5f + 0.004f, 0f);

                TMP_Text readout = screen.GetComponentInChildren<TMP_Text>(true);
                if (readout != null)
                {
                    readout.enableAutoSizing = true;
                    readout.fontSizeMin = 14;
                    readout.fontSizeMax = 30;
                    readout.fontSize = 26;
                    readout.color = new Color(0.6f, 1f, 0.88f);
                    EditorUtility.SetDirty(readout);
                }

                Transform housing = FindChild(scanner.transform, "Screen Housing");
                if (housing == null)
                {
                    GameObject h = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    h.name = "Screen Housing";
                    Object.DestroyImmediate(h.GetComponent<Collider>());
                    housing = h.transform;
                }
                housing.SetParent(hinge, false);
                housing.localRotation = Quaternion.identity;
                housing.localPosition = rt.localPosition + new Vector3(0f, 0f, 0.0045f); // just behind the canvas
                housing.localScale = new Vector3(w * mpp + 0.008f, hgt * mpp + 0.008f, 0.006f);
                var dark = AssetDatabase.LoadAssetAtPath<Material>(GenRoot + "/Materials/ContainerDark.mat");
                if (dark != null) housing.GetComponent<Renderer>().sharedMaterial = dark;
            }

            if (wristScan != null) SetList(scanner, "mirrorTexts", wristScan);
            EditorUtility.SetDirty(scanner);
            log.Add("Scanner: bigger self-tilting screen, wrist mirror, velocity tracking (no clipping)");
            return scanner;
        }

        private static TMP_Text FixWristDisplay(XROrigin origin, List<string> log)
        {
            if (origin == null) return null;
            Transform wrist = FindChild(origin.transform, "Wrist Display");
            if (!(wrist is RectTransform c)) { log.Add("SKIPPED wrist display: not found"); return null; }

            c.sizeDelta = new Vector2(340, 260);
            Anchor(FindChild(c, "Timer"), 0.72f, 1f, 0f);
            Anchor(FindChild(c, "Samples"), 0.55f, 0.72f, 0f);
            Anchor(FindChild(c, "Comms"), 0f, 0.30f, 0.03f);

            Transform scanT = FindChild(c, "Scan");
            TMP_Text scan;
            if (scanT == null)
            {
                var go = new GameObject("Scan", typeof(RectTransform));
                go.transform.SetParent(c, false);
                scan = go.AddComponent<TextMeshProUGUI>();
                scan.raycastTarget = false;
            }
            else scan = scanT.GetComponent<TMP_Text>();
            Anchor(scan.transform, 0.30f, 0.55f, 0.03f);
            scan.text = "SCAN: -";
            scan.fontSize = 17;
            scan.enableAutoSizing = true;
            scan.fontSizeMin = 10;
            scan.fontSizeMax = 18;
            scan.alignment = TextAlignmentOptions.Center;
            scan.color = new Color(0.6f, 1f, 0.85f);
            log.Add("Wrist display: added SCAN readout line");
            return scan;
        }

        private static void Anchor(Transform t, float yMin, float yMax, float xPad)
        {
            if (!(t is RectTransform rt)) return;
            rt.anchorMin = new Vector2(xPad, yMin);
            rt.anchorMax = new Vector2(1f - xPad, yMax);
            rt.offsetMin = new Vector2(10, 4);
            rt.offsetMax = new Vector2(-10, -4);
        }

        private static void FixHolster(XROrigin origin, GeoScanner scanner, List<string> log)
        {
            if (origin == null || scanner == null) return;
            Transform holster = FindChild(origin.transform, "Scanner Holster (belt)");
            if (holster == null)
            {
                holster = new GameObject("Scanner Holster (belt)").transform;
                holster.SetParent(origin.transform, false);
            }
            var col = GetOrAdd<SphereCollider>(holster.gameObject);
            col.isTrigger = true;
            col.radius = 0.14f;
            var rb = GetOrAdd<Rigidbody>(holster.gameObject);
            rb.isKinematic = true;
            rb.useGravity = false;

            Transform attach = FindChild(holster, "Holster Attach");
            if (attach == null)
            {
                attach = new GameObject("Holster Attach").transform;
                attach.SetParent(holster, false);
            }
            attach.localPosition = Vector3.zero;
            attach.localRotation = Quaternion.Euler(70f, 0f, 0f); // scanner hangs nose-down at the hip

            var socket = GetOrAdd<XRSocketInteractor>(holster.gameObject);
            socket.attachTransform = attach;
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0f;
            EditorUtility.SetDirty(socket);

            var h = GetOrAdd<ToolHolster>(holster.gameObject);
            Set(h, "tool", scanner);

            if (FindChild(holster, "Belt Clip") == null)
            {
                GameObject clip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                clip.name = "Belt Clip";
                Object.DestroyImmediate(clip.GetComponent<Collider>());
                clip.transform.SetParent(holster, false);
                clip.transform.localPosition = new Vector3(0f, 0.02f, -0.03f);
                clip.transform.localScale = new Vector3(0.05f, 0.08f, 0.02f);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(GenRoot + "/Materials/ScannerAccent.mat");
                if (mat != null) clip.GetComponent<Renderer>().sharedMaterial = mat;
            }
            log.Add("Scanner holster on the player's belt (auto-return when released)");
        }

        // ============================================================================ SKY

        private static Transform FixSky(List<string> log)
        {
            Transform env = Root("[Environment]");
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectional();

            // ---- Sun disc ----
            Transform disc = FindChild(env, "Sun Disc");
            if (disc == null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Sun Disc";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(env, false);
                disc = q.transform;
            }
            Texture2D sunTex = MakeSunTexture();
            Material sunMat = TransparentMaterial("SunDisc", "Universal Render Pipeline/Unlit", sunTex, additive: true, Color.white);
            var r = disc.GetComponent<MeshRenderer>();
            r.sharedMaterial = sunMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // Disc drawn ~1.2 deg wide (real Sun: 0.53 deg; a bit larger so it reads in a headset).
            // The texture's disc is 20 % of the quad width, the rest is glare.
            const float sunDistance = 800f;
            float discDiameter = sunDistance * Mathf.Tan(1.2f * Mathf.Deg2Rad);
            disc.localScale = Vector3.one * (discDiameter / 0.2f);
            var cb = GetOrAdd<CelestialBody>(disc.gameObject);
            SetEnum(cb, "mode", (int)CelestialBody.Mode.FollowLight);
            Set(cb, "lightSource", sun);
            SetFloat(cb, "distance", sunDistance);
            SetBool(cb, "billboard", true);
            if (sun != null)
            {
                disc.position = -sun.transform.forward * sunDistance;
                disc.rotation = Quaternion.LookRotation(-sun.transform.forward, Vector3.up);
            }

            // ---- Earth: real texture + lit by the Sun, so it shows a true phase ----
            Transform earth = FindChild(env, "Earth");
            if (earth != null)
            {
                Texture2D earthTex = ImportTexture(EarthTexturePath, 2048, TextureWrapMode.Clamp, true);
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                var m = new Material(lit) { name = "EarthLit" };
                m.SetColor("_BaseColor", earthTex != null ? Color.white : new Color(0.35f, 0.55f, 0.95f));
                if (earthTex != null) m.SetTexture("_BaseMap", earthTex);
                m.SetFloat("_Smoothness", 0.3f);
                m.SetFloat("_Metallic", 0f);
                m = SaveAsset(m, GenRoot + "/Materials/EarthLit.mat");
                var er = earth.GetComponent<MeshRenderer>();
                er.sharedMaterial = m;
                er.shadowCastingMode = ShadowCastingMode.Off;
                er.receiveShadows = false;
                earth.localScale = Vector3.one * 16f; // ~2 deg wide from 450 m (real: 1.9 deg)
                earth.rotation = Quaternion.Euler(0f, 0f, 23.4f) * Quaternion.Euler(0f, 160f, 0f);
                var ecb = GetOrAdd<CelestialBody>(earth.gameObject);
                SetEnum(ecb, "mode", (int)CelestialBody.Mode.FixedDirection);
                SetVector(ecb, "direction", EarthDirection);
                SetFloat(ecb, "distance", 450f);
                earth.position = EarthDirection.normalized * 450f;
                log.Add(earthTex != null
                    ? "Earth: NASA Blue Marble texture, lit by the Sun (shows a real phase), fixed at infinity"
                    : "Earth: lit material (texture missing at " + EarthTexturePath + ")");
            }

            log.Add("Sun: visible disc + glare, locked to the Directional Light");
            return disc;
        }

        private static Texture2D MakeSunTexture()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            const float rd = 0.2f; // disc radius (1 = quad half-width)
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    Color c;
                    if (r <= rd)
                    {
                        float mu = Mathf.Sqrt(1f - (r / rd) * (r / rd));
                        float limb = 1f - 0.45f * (1f - mu); // limb darkening
                        c = new Color(1f, 0.985f, 0.95f, 1f) * limb;
                        c.a = 1f;
                    }
                    else
                    {
                        float d = r - rd;
                        float a = 0.6f * Mathf.Exp(-d / 0.035f) + 0.18f * Mathf.Exp(-d / 0.22f);
                        a *= Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * 1.6f;
                        c = new Color(1f, 0.96f, 0.88f, Mathf.Clamp01(a));
                    }
                    px[y * n + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return SaveTexture(tex, GenRoot + "/Textures/SunDisc.png");
        }

        // ============================================================================ STORM

        private static void FixStorm(GameObject managers, Camera cam, MissionComms comms, Transform sunDisc, List<string> log)
        {
            StormTimer storm = managers.GetComponent<StormTimer>();
            if (storm == null) return;
            StormEffects fx = GetOrAdd<StormEffects>(managers);
            Set(fx, "storm", storm);
            Set(fx, "comms", comms);
            Set(fx, "sunLight", RenderSettings.sun != null ? RenderSettings.sun : FindDirectional());
            Set(fx, "sunDisc", sunDisc);

            // The Sun does not turn orange in a vacuum - keep the tint subtle (reads as the gold visor).
            SetColor(storm, "stormSunColor", new Color(1f, 0.93f, 0.82f));
            SetFloat(storm, "rumbleMaxVolume", 0.35f);
            SetFloat(fx, "maxVisorAlpha", 0.18f);

            Transform mission = Root("[Mission]");
            Texture2D dot = MakeDotTexture();

            // ---- Levitating dust (around the player, at ground level) ----
            Transform dustT = FindChild(mission, "Storm FX - Levitating Dust");
            if (dustT == null)
            {
                dustT = new GameObject("Storm FX - Levitating Dust").transform;
                dustT.SetParent(mission, false);
            }
            dustT.rotation = Quaternion.Euler(-90f, 0f, 0f); // emit upward
            ParticleSystem dust = GetOrAdd<ParticleSystem>(dustT.gameObject);
            {
                var main = dust.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.025f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.84f, 0.8f, 0.55f), new Color(1f, 0.97f, 0.9f, 0.85f));
                main.gravityModifier = 1f; // Physics.gravity is already Moon gravity -> slow ballistic hops
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 4000;
                var em = dust.emission; em.rateOverTime = 0f;
                var sh = dust.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(16f, 16f, 0.05f);
                sh.randomDirectionAmount = 0.25f;
                var col = dust.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var collision = dust.collision;
                collision.enabled = false;
                var pr = dust.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.sharedMaterial = TransparentMaterial("StormDust", "Universal Render Pipeline/Particles/Unlit", dot, additive: true, Color.white);
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
            }

            // ---- Cosmic-ray light flashes (in front of the eyes) ----
            ParticleSystem flashes = null;
            Renderer visor = null;
            if (cam != null)
            {
                Transform fT = FindChild(cam.transform, "Storm FX - Cosmic Ray Flashes");
                if (fT == null)
                {
                    fT = new GameObject("Storm FX - Cosmic Ray Flashes").transform;
                    fT.SetParent(cam.transform, false);
                }
                fT.localPosition = new Vector3(0f, 0f, 0.5f);
                fT.localRotation = Quaternion.identity;
                flashes = GetOrAdd<ParticleSystem>(fT.gameObject);
                var main = flashes.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.006f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.9f, 1f, 1f), Color.white);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 50;
                var em = flashes.emission; em.rateOverTime = 0f;
                var sh = flashes.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(0.7f, 0.45f, 0.05f);
                sh.randomDirectionAmount = 1f;
                var pr = flashes.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.lengthScale = 4f;
                pr.velocityScale = 0.02f;
                pr.sharedMaterial = TransparentMaterial("CosmicFlash", "Universal Render Pipeline/Particles/Unlit", dot, additive: true, Color.white);
                pr.shadowCastingMode = ShadowCastingMode.Off;

                // ---- Gold visor tint at the edges of the view ----
                Transform vT = FindChild(cam.transform, "Storm FX - Visor Tint");
                if (vT == null)
                {
                    GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "Storm FX - Visor Tint";
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    q.transform.SetParent(cam.transform, false);
                    vT = q.transform;
                }
                vT.localPosition = new Vector3(0f, 0f, 0.06f);
                vT.localRotation = Quaternion.identity;
                vT.localScale = Vector3.one * 0.3f;
                visor = vT.GetComponent<Renderer>();
                visor.sharedMaterial = TransparentMaterial("StormVisor", "Universal Render Pipeline/Unlit", MakeVisorTexture(), additive: false, new Color(1f, 0.72f, 0.3f, 0f));
                visor.shadowCastingMode = ShadowCastingMode.Off;
                visor.receiveShadows = false;
                visor.enabled = false;
            }

            AudioSource dosimeter = ChildAudio(managers.transform, "Dosimeter (2D, suit)", loop: false, volume: 1f);

            Set(fx, "levitatingDust", dust);
            Set(fx, "cosmicFlashes", flashes);
            Set(fx, "visorOverlay", visor);
            Set(fx, "dosimeterSource", dosimeter);
            Set(fx, "geigerClick", Sfx("geiger_click"));
            log.Add("Storm: radiation effects (dosimeter, radio static, cosmic-ray flashes, levitating dust, visor tint)");
        }

        private static Texture2D MakeDotTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return SaveTexture(tex, GenRoot + "/Textures/SoftDot.png");
        }

        private static Texture2D MakeVisorTexture()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.6f, r));
                    px[y * n + x] = new Color(1f, 1f, 1f, 0.06f + 0.94f * edge);
                }
            tex.SetPixels(px);
            tex.Apply();
            return SaveTexture(tex, GenRoot + "/Textures/StormVisor.png");
        }

        // ============================================================================ AUDIO

        private static void FixAudio(GameObject managers, MissionComms comms, GeoScanner scanner, List<string> log)
        {
            if (Sfx("scan_beep") == null)
            {
                log.Add("AUDIO SKIPPED: no clips in " + SfxFolder + " - run  python3 Tools/generate_sfx.py  then this menu again");
                return;
            }

            if (scanner != null)
            {
                Set(scanner, "scanClip", Sfx("scan_beep"));
                Set(scanner, "targetClip", Sfx("scan_match"));
            }

            SampleContainer container = FindInScene<SampleContainer>();
            if (container != null)
            {
                Set(container, "lockClip", Sfx("sample_lock"));
                Set(container, "lidCloseClip", Sfx("lid_close"));
                foreach (SampleSocketFilter f in container.GetComponentsInChildren<SampleSocketFilter>(true))
                {
                    Set(f, "rejectClip", Sfx("sample_reject"));
                }
            }

            if (comms != null)
            {
                AddMissingCommsLines(comms);
                Set(comms, "radioBeep", Sfx("quindar_tone"));
                int voice = AssignRecordedLines(comms);
                if (voice > 0) log.Add($"Houston: assigned {voice} recorded voice line(s) from {CommsFolder}");
            }

            StormTimer storm = managers.GetComponent<StormTimer>();
            if (storm != null)
            {
                var so = new SerializedObject(storm);
                var src = so.FindProperty("stormRumble").objectReferenceValue as AudioSource;
                if (src != null)
                {
                    src.clip = Sfx("radio_static_loop");
                    src.loop = true;
                    src.spatialBlend = 0f;
                    src.gameObject.name = "Storm Radio Static (2D)";
                    EditorUtility.SetDirty(src);
                }
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "[Lander]") continue;
                AudioSource beacon = root.GetComponent<AudioSource>();
                if (beacon == null) continue;
                beacon.clip = Sfx("lander_beacon_loop");
                beacon.loop = true;
                beacon.playOnAwake = true;
                EditorUtility.SetDirty(beacon);
            }

            AudioSource suit = ChildAudio(managers.transform, "Suit Ambience (2D, life-support fan)", loop: true, volume: 0.12f);
            suit.clip = Sfx("suit_fan_loop");
            suit.playOnAwake = true;

            log.Add("Audio: 13 generated SFX assigned (scanner, container, Quindar tone, static, beacon, dosimeter, footsteps, suit fan)");
        }

        /// <summary>Appends new default Houston lines (e.g. boundary_warning) to the scene's list without touching edited ones.</summary>
        private static void AddMissingCommsLines(MissionComms comms)
        {
            var so = new SerializedObject(comms);
            SerializedProperty lines = so.FindProperty("lines");
            var existing = new HashSet<string>();
            for (int i = 0; i < lines.arraySize; i++) existing.Add(lines.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue);
            foreach (CommsLine d in MissionComms.DefaultLines())
            {
                if (existing.Contains(d.id)) continue;
                lines.arraySize++;
                SerializedProperty p = lines.GetArrayElementAtIndex(lines.arraySize - 1);
                p.FindPropertyRelative("id").stringValue = d.id;
                p.FindPropertyRelative("subtitle").stringValue = d.subtitle;
                p.FindPropertyRelative("holdSeconds").floatValue = d.holdSeconds;
                p.FindPropertyRelative("clip").objectReferenceValue = null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int AssignRecordedLines(MissionComms comms)
        {
            if (!AssetDatabase.IsValidFolder(CommsFolder)) return 0;
            var so = new SerializedObject(comms);
            SerializedProperty lines = so.FindProperty("lines");
            int count = 0;
            for (int i = 0; i < lines.arraySize; i++)
            {
                SerializedProperty line = lines.GetArrayElementAtIndex(i);
                string id = line.FindPropertyRelative("id").stringValue;
                SerializedProperty clipProp = line.FindPropertyRelative("clip");
                foreach (string ext in new[] { ".wav", ".aiff", ".aif", ".mp3", ".ogg", ".m4a" })
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{CommsFolder}/{id}{ext}");
                    if (clip == null) continue;
                    clipProp.objectReferenceValue = clip;
                    count++;
                    break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }

        private static AudioClip Sfx(string name)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxFolder}/{name}.wav");
        }

        private static AudioSource ChildAudio(Transform parent, string name, bool loop, float volume)
        {
            Transform t = FindChild(parent, name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            AudioSource s = GetOrAdd<AudioSource>(t.gameObject);
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.volume = volume;
            return s;
        }

        // ============================================================================ BOUNDARY

        private static void FixBoundary(List<string> log)
        {
            Transform env = Root("[Environment]");
            Transform old = FindChild(env, "Play Area Boundary");
            if (old != null) Object.DestroyImmediate(old.gameObject); // fully generated, safe to rebuild
            var root = new GameObject("Play Area Boundary").transform;
            root.SetParent(env, false);

            Terrain terrain = FindInScene<Terrain>();
            Material marker = LitMaterial("BoundaryMarker", new Color(1f, 0.45f, 0.1f));
            const int segments = 48;
            const float thickness = 1f, height = 14f;
            float chord = 2f * Mathf.PI * (WallRadius + thickness * 0.5f) / segments * 1.15f;

            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = PlayAreaCentre + dir * (WallRadius + thickness * 0.5f);
                float gy = GroundY(terrain, p);

                var wall = new GameObject($"Wall {i + 1:00}");
                wall.transform.SetParent(root, false);
                wall.transform.position = new Vector3(p.x, gy + height * 0.5f - 3f, p.z);
                wall.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                var box = wall.AddComponent<BoxCollider>();
                box.size = new Vector3(chord, height, thickness);

                if (i % 3 == 0) // survey stakes so players can SEE where the zone ends
                {
                    Vector3 sp = PlayAreaCentre + dir * (WallRadius - 0.5f);
                    float sy = GroundY(terrain, sp);
                    GameObject stake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    stake.name = "Marker Stake";
                    Object.DestroyImmediate(stake.GetComponent<Collider>());
                    stake.transform.SetParent(root, false);
                    stake.transform.position = new Vector3(sp.x, sy + 0.35f, sp.z);
                    stake.transform.localScale = new Vector3(0.025f, 0.35f, 0.025f);
                    stake.GetComponent<Renderer>().sharedMaterial = marker;
                    GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    flag.name = "Flag";
                    Object.DestroyImmediate(flag.GetComponent<Collider>());
                    flag.transform.SetParent(stake.transform, false);
                    flag.transform.localPosition = new Vector3(0f, 0.85f, 3f);
                    flag.transform.localScale = new Vector3(0.4f, 0.25f, 6f);
                    flag.GetComponent<Renderer>().sharedMaterial = marker;
                }
            }
            log.Add($"Boundary: invisible wall ring (r = {WallRadius} m) + orange survey stakes; rocks are all within ~18 m");
        }

        private static float GroundY(Terrain t, Vector3 p)
        {
            return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }

        // ============================================================================ RENDERING

        private static void FixShadows(List<string> log)
        {
            foreach (RenderPipelineAsset rp in new[] { GraphicsSettings.defaultRenderPipeline, QualitySettings.renderPipeline })
            {
                if (rp is UniversalRenderPipelineAsset urp && urp.shadowDistance < 40f)
                {
                    urp.shadowDistance = 40f;
                    EditorUtility.SetDirty(urp);
                    log.Add($"Rendering: shadow distance -> 40 m on {urp.name} (was 2.5 m, so most rocks had no shadow)");
                }
            }
        }

        // ============================================================================ HELPERS

        private static Material TransparentMaterial(string name, string shaderName, Texture tex, bool additive, Color color)
        {
            Shader shader = Shader.Find(shaderName);
            var m = new Material(shader) { name = name };
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return SaveAsset(m, $"{GenRoot}/Materials/{name}.mat");
        }

        private static Material LitMaterial(string name, Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.3f);
            return SaveAsset(m, $"{GenRoot}/Materials/{name}.mat");
        }

        private static T SaveAsset<T>(T asset, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Texture2D SaveTexture(Texture2D tex, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return ImportTexture(path, 512, TextureWrapMode.Clamp, true, alpha: true);
        }

        private static Texture2D ImportTexture(string path, int maxSize, TextureWrapMode wrap, bool mipmaps, bool alpha = false)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                bool changed = imp.maxTextureSize != maxSize || imp.wrapMode != wrap || imp.mipmapEnabled != mipmaps || imp.alphaIsTransparency != alpha;
                imp.maxTextureSize = maxSize;
                imp.wrapMode = wrap;
                imp.mipmapEnabled = mipmaps;
                imp.alphaIsTransparency = alpha;
                if (changed) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static Transform Root(string name)
        {
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == name) return go.transform;
            }
            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, SceneManager.GetActiveScene());
            return created.transform;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static T FindInScene<T>() where T : Component
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static Light FindDirectional()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Directional) return l;
            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static SerializedProperty Prop(Object target, string field, out SerializedObject so)
        {
            so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) Debug.LogError($"[LunarPlaytestFixes] {target.GetType().Name} has no field '{field}'.");
            return p;
        }

        private static void Set(Object target, string field, Object value)
        {
            if (target == null) return;
            var p = Prop(target, field, out var so); if (p == null) return;
            p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float v)
        {
            var p = Prop(target, field, out var so); if (p == null) return;
            p.floatValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool v)
        {
            var p = Prop(target, field, out var so); if (p == null) return;
            p.boolValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int v)
        {
            var p = Prop(target, field, out var so); if (p == null) return;
            p.enumValueIndex = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(Object target, string field, Vector3 v)
        {
            var p = Prop(target, field, out var so); if (p == null) return;
            p.vector3Value = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColor(Object target, string field, Color v)
        {
            var p = Prop(target, field, out var so); if (p == null) return;
            p.colorValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetList(Object target, string field, params Object[] values)
        {
            if (target == null) return;
            var p = Prop(target, field, out var so); if (p == null || !p.isArray) return;
            var clean = new List<Object>();
            foreach (Object v in values) if (v != null) clean.Add(v);
            p.arraySize = clean.Count;
            for (int i = 0; i < clean.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = clean[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
