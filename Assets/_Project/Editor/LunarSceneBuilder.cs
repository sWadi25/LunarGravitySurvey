using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LunarSurvey.EditorTools
{
    /// <summary>
    /// One-click scene builder for The Lunar Gravity Survey.
    /// Menu: Lunar Survey > Build Everything   (or run the steps one by one).
    ///
    /// It creates three root objects in the OPEN scene:
    ///   [Environment]  terrain with craters, starfield sky, Earth, low sun   (keeps an existing Terrain)
    ///   [Lander]       the lander model, rescaled + re-centred, collider, radio beacon
    ///   [Mission]      managers, sample container, rocks, scanner, every world-space panel - all wired up
    ///
    /// Everything it generates (meshes, textures, materials) is saved under Assets/_Project/Generated,
    /// so it is your team's own original asset work. Re-running a step deletes and rebuilds that root,
    /// so DO NOT hand-edit inside [Mission] until you are happy with the generated layout (or stop
    /// re-running the step after you start hand-editing).
    /// </summary>
    public static class LunarSceneBuilder
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string GenRoot = ProjectRoot + "/Generated";
        private const string LanderFolder = ProjectRoot + "/Models/Lander";
        private const string CreditsPath = ProjectRoot + "/Data/Credits.txt";

        // ---- Layout (world space). The player starts at the origin, facing +Z. ----
        private static readonly Vector3 PlayerStart = Vector3.zero;
        private static readonly Vector3 PlayAreaCentre = new Vector3(-1.5f, 0f, 6f);
        private const float FlatRadius = 24f;                 // radius of the flat, teleport-friendly area
        private static readonly Vector3 LanderPos = new Vector3(-7f, 0f, 12f);
        private const float LanderHeight = 7f;               // real Apollo LM: ~7 m tall, ~9.4 m leg span
        // PLACEHOLDER: if the lander model comes in upside down / on its side, change this (e.g. (180,0,0) or (-90,0,0))
        // and run Lunar Survey > Steps > 2. Lander again.
        private static readonly Vector3 LanderModelEuler = Vector3.zero;
        private static readonly Vector3 ContainerPos = new Vector3(-1.4f, 0f, 5.6f);
        private static readonly Vector3 ToolStandPos = new Vector3(0.9f, 0f, 5.2f);
        private static readonly Vector3 BriefingPos = new Vector3(0f, 1.45f, 1.9f);
        private static readonly Vector3 LocoPanelPos = new Vector3(2.4f, 1.35f, 3.6f);
        private static readonly Vector3 CreditsPos = new Vector3(-2.6f, 1.5f, 2.6f);
        private static readonly Vector3 EarthDirection = new Vector3(0.35f, 0.45f, 1f);

        private static readonly Vector2[] RockSpots =
        {
            new Vector2(6.5f, 4f), new Vector2(8f, 11f), new Vector2(4f, 16f), new Vector2(-3f, 19f),
            new Vector2(-14f, 18f), new Vector2(-15f, 7f), new Vector2(-12f, -3f), new Vector2(-4f, -7f),
            new Vector2(5f, -5f), new Vector2(12f, 4f)
        };

        // Craters outside the play area: x, z, radius, depth (metres)
        private static readonly Vector4[] Craters =
        {
            new Vector4(30, 40, 12, 3), new Vector4(-35, 30, 9, 2.5f), new Vector4(-28, -30, 15, 4),
            new Vector4(40, -20, 10, 3), new Vector4(5, -45, 8, 2), new Vector4(-55, 0, 18, 5),
            new Vector4(60, 35, 14, 3.5f), new Vector4(15, 60, 20, 5), new Vector4(-20, 70, 10, 2.5f),
            new Vector4(70, -55, 16, 4), new Vector4(-60, -60, 22, 6), new Vector4(45, 75, 12, 3)
        };

        // ============================================================================ MENU

        [MenuItem("Lunar Survey/Build Everything", priority = 0)]
        public static void BuildEverything()
        {
            if (!Confirm("Build Everything",
                    "This adds/rebuilds [Environment], [Lander] and [Mission] in the open scene.\n\n" +
                    "An existing Terrain is kept. Commit your work to Git first so you can undo.")) return;
            BuildEnvironment();
            BuildLander();
            BuildMission();
            Finish();
        }

        [MenuItem("Lunar Survey/Steps/1. Environment (terrain, sky, sun, Earth)", priority = 20)]
        public static void MenuEnvironment() { if (Confirm("Environment", "Rebuild [Environment]? An existing Terrain is kept.")) { BuildEnvironment(); Finish(); } }

        [MenuItem("Lunar Survey/Steps/2. Lander", priority = 21)]
        public static void MenuLander() { if (Confirm("Lander", "Rebuild [Lander] from the model in " + LanderFolder + "?")) { BuildLander(); Finish(); } }

        [MenuItem("Lunar Survey/Steps/3. Mission (container, rocks, scanner, UI, managers)", priority = 22)]
        public static void MenuMission() { if (Confirm("Mission", "Rebuild [Mission]? Hand edits inside [Mission] will be lost.")) { BuildMission(); Finish(); } }

        private static bool Confirm(string title, string message)
        {
            return EditorUtility.DisplayDialog("Lunar Survey - " + title, message, "Build", "Cancel");
        }

        private static void Finish()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                scenes.RemoveAll(x => x.path == scene.path);
                foreach (EditorBuildSettingsScene s in scenes) s.enabled = false; // only the mission scene ships
                scenes.Insert(0, new EditorBuildSettingsScene(scene.path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            // Play-test fixes (scanner holster, terrain follow, boundary, Sun, storm FX, audio...). Idempotent.
            LunarPlaytestFixes.ApplyAll();

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[LunarSceneBuilder] Done. Press Ctrl+S (Cmd+S) to save the scene, then press Play.");
        }

        // ============================================================================ 1. ENVIRONMENT

        private static void BuildEnvironment()
        {
            Terrain existingTerrain = FindInScene<Terrain>();
            DestroyRoot("[Environment]", keep: existingTerrain != null ? existingTerrain.transform : null);
            GameObject root = NewRoot("[Environment]");

            // Terrain
            Terrain terrain = existingTerrain != null ? existingTerrain : CreateTerrain(root.transform);
            AddTeleportArea(terrain.gameObject);

            // Sky
            Texture2D stars = MakeStarfield();
            Shader panoramic = Shader.Find("Skybox/Panoramic");
            if (panoramic != null)
            {
                var sky = new Material(panoramic) { name = "LunarStarfieldSky" };
                sky.SetTexture("_MainTex", stars);
                sky.SetFloat("_Exposure", 1f);
                RenderSettings.skybox = SaveAsset(sky, GenRoot + "/Materials/LunarStarfieldSky.mat");
            }
            // PLACEHOLDER (optional upgrade): replace the generated starfield with NASA SVS "Deep Star Maps 2020"
            // by assigning that image to the LunarStarfieldSky material's Spherical (HDR) slot. Credit NASA if you do.

            // No atmosphere: no fog, very dark ambient light, hard low sun
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.07f);

            Light sun = FindDirectionalLight();
            if (sun == null)
            {
                var sunGo = new GameObject("Sun (Directional Light)");
                sunGo.transform.SetParent(root.transform, false);
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.color = Color.white;
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.Euler(14f, -35f, 0f); // low sun = long Apollo-style shadows
            RenderSettings.sun = sun;

            // Earth hanging in the sky (strong presence cue)
            GameObject earth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            earth.name = "Earth";
            Object.DestroyImmediate(earth.GetComponent<Collider>());
            earth.transform.SetParent(root.transform, false);
            earth.transform.position = EarthDirection.normalized * 450f;
            earth.transform.localScale = Vector3.one * 20f;
            earth.transform.rotation = Quaternion.Euler(0f, 0f, 23.4f);
            Material earthMat = UnlitMaterial("Earth", new Color(0.35f, 0.55f, 0.95f));
            // PLACEHOLDER: assign a NASA "Blue Marble" image to Assets/_Project/Generated/Materials/Earth.mat > Base Map.
            earth.GetComponent<MeshRenderer>().sharedMaterial = earthMat;
            earth.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The scene was copied from the template's SampleScene, so it still points at that scene's baked
            // lighting (a bright white room). Drop it, otherwise rocks and props are lit by light probes and
            // reflections of a room that isn't there.
            Lightmapping.Clear();
            Lightmapping.lightingDataAsset = null;
            foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (LightProbeGroup g in sceneRoot.GetComponentsInChildren<LightProbeGroup>(true)) g.gameObject.SetActive(false);
                foreach (ReflectionProbe r in sceneRoot.GetComponentsInChildren<ReflectionProbe>(true)) r.gameObject.SetActive(false);
            }
            // OPTIONAL polish later: Window > Rendering > Lighting > Generate Lighting, once the layout is final.

            DynamicGI.UpdateEnvironment();
        }

        private static Terrain CreateTerrain(Transform parent)
        {
            const int res = 257;
            const float size = 240f;
            const float heightRange = 40f;
            const float baseHeight = 10f; // the plain sits at world y = 0, craters can go below it

            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(size, heightRange, size);

            var heights = new float[res, res];
            for (int zi = 0; zi < res; zi++)
            {
                for (int xi = 0; xi < res; xi++)
                {
                    float wx = -size / 2f + xi / (res - 1f) * size;
                    float wz = -size / 2f + zi / (res - 1f) * size;
                    float distFromPlay = new Vector2(wx - PlayAreaCentre.x, wz - PlayAreaCentre.z).magnitude;
                    float rough = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatRadius, FlatRadius + 14f, distFromPlay));

                    float h = baseHeight;
                    h += (Mathf.PerlinNoise(wx * 0.25f + 13f, wz * 0.25f + 7f) - 0.5f) * 0.08f;          // tiny surface bumps
                    h += (Mathf.PerlinNoise(wx * 0.035f + 3f, wz * 0.035f + 91f) - 0.5f) * 3.5f * rough; // rolling ground

                    foreach (Vector4 c in Craters)
                    {
                        float d = new Vector2(wx - c.x, wz - c.y).magnitude / c.z;
                        float crater = 0f;
                        if (d < 1f) crater = -c.w * (1f - d * d) + c.w * 0.25f;
                        else if (d < 1.6f) crater = c.w * 0.25f * Mathf.Pow(1f - (d - 1f) / 0.6f, 2f);
                        h += crater * rough;
                    }

                    float edge = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                    h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(85f, 118f, edge)) * 16f; // ridges stop players leaving

                    heights[zi, xi] = Mathf.Clamp01(h / heightRange);
                }
            }
            data.SetHeights(0, 0, heights);

            var layer = new TerrainLayer
            {
                diffuseTexture = MakeRegolithTexture(),
                tileSize = new Vector2(6f, 6f),
                smoothness = 0.05f,
                metallic = 0f
            };
            layer = SaveAsset(layer, GenRoot + "/Terrain/RegolithLayer.terrainlayer");
            data.terrainLayers = new[] { layer };
            data = SaveAsset(data, GenRoot + "/Terrain/LunarTerrainData.asset");

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Lunar Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-size / 2f, -baseHeight, -size / 2f);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go.GetComponent<Terrain>();
        }

        private static void AddTeleportArea(GameObject ground)
        {
            TeleportationArea area = ground.GetComponent<TeleportationArea>();
            if (area == null) area = ground.AddComponent<TeleportationArea>();
            int mask = InteractionLayerMask.GetMask("Teleport");
            area.interactionLayers = mask != 0 ? mask : -1;
        }

        // ============================================================================ 2. LANDER

        private static void BuildLander()
        {
            DestroyRoot("[Lander]");
            GameObject root = NewRoot("[Lander]");
            root.transform.position = Ground(LanderPos);

            GameObject model = FindLanderModel();
            GameObject inst;
            if (model != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.Euler(LanderModelEuler);
            }
            else
            {
                Debug.LogWarning("[LunarSceneBuilder] No lander model in " + LanderFolder + " - built a placeholder. " +
                                 "Drop your .fbx/.glb there and run Steps > 2. Lander again.");
                inst = BuildPlaceholderLander(root.transform);
            }

            // Fit: the Sketchfab FBX is ~19 m wide with its pivot ~50 m away from the mesh.
            // Scale to a realistic height, then move it so its base sits centred on [Lander].
            Bounds b = RendererBounds(inst);
            if (b.size.y > 0.001f)
            {
                inst.transform.localScale *= LanderHeight / b.size.y;
                b = RendererBounds(inst);
                inst.transform.position += root.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
                b = RendererBounds(inst);
            }
            // If the model appears upside down or lying on its side, see LanderModelEuler at the top of this file.

            // One simple box for the body (cheap, and stops teleporting/walking inside the lander).
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x * 0.7f, b.size.y, b.size.z * 0.7f);

            root.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            // Radio beacon: a looping 3D beep that helps players find their way back (spatial audio with a job).
            AudioSource beacon = root.AddComponent<AudioSource>();
            beacon.playOnAwake = true;
            beacon.loop = true;
            beacon.spatialBlend = 1f;
            beacon.rolloffMode = AudioRolloffMode.Linear;
            beacon.minDistance = 2f;
            beacon.maxDistance = 45f;
            beacon.volume = 0.5f;
            // PLACEHOLDER: assign a short looping beacon 'ping' clip to [Lander] > Audio Source > AudioClip.
        }

        private static GameObject FindLanderModel()
        {
            if (!AssetDatabase.IsValidFolder(LanderFolder)) return null;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { LanderFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null) return go;
            }
            return null;
        }

        private static GameObject BuildPlaceholderLander(Transform parent)
        {
            var go = new GameObject("PLACEHOLDER Lander (replace with model)");
            go.transform.SetParent(parent, false);
            Material gold = LitMaterial("LanderFoil", new Color(0.8f, 0.62f, 0.25f), 0.6f, 0.8f);
            Primitive(PrimitiveType.Cylinder, "Descent Stage", go.transform, new Vector3(0, 2.2f, 0), new Vector3(4f, 1.2f, 4f), gold);
            Primitive(PrimitiveType.Cube, "Ascent Stage", go.transform, new Vector3(0, 4.3f, 0), new Vector3(3f, 2.4f, 3f), LitMaterial("LanderWhite", new Color(0.8f, 0.8f, 0.82f), 0.3f, 0.2f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                GameObject leg = Primitive(PrimitiveType.Cylinder, "Leg", go.transform, dir * 3.2f + Vector3.up * 1.2f, new Vector3(0.15f, 1.4f, 0.15f), gold);
                leg.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(60f, 0, 0);
            }
            return go;
        }

        // ============================================================================ 3. MISSION

        private static void BuildMission()
        {
            DestroyRoot("[Mission]");
            GameObject root = NewRoot("[Mission]");

            // ---------- Managers ----------
            GameObject managers = Child(root.transform, "Managers");
            managers.AddComponent<LunarGravity>();
            MissionManager mission = managers.AddComponent<MissionManager>();
            StormTimer storm = managers.AddComponent<StormTimer>();
            MissionHUD hud = managers.AddComponent<MissionHUD>();
            LocomotionModeController locomotion = managers.AddComponent<LocomotionModeController>();
            DesktopFallbackRig desktop = managers.AddComponent<DesktopFallbackRig>();

            GameObject commsGo = Child(managers.transform, "Mission Comms (helmet radio)");
            AudioSource voice = commsGo.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            MissionComms comms = commsGo.AddComponent<MissionComms>();

            GameObject rumbleGo = Child(managers.transform, "Storm Rumble (2D ambient)");
            AudioSource rumble = rumbleGo.AddComponent<AudioSource>();
            rumble.playOnAwake = false;
            rumble.loop = true;
            rumble.spatialBlend = 0f;
            // PLACEHOLDER: assign a low rumble loop to 'Storm Rumble (2D ambient)' > AudioClip.

            // ---------- Physical objects ----------
            SampleContainer container = BuildContainer(root.transform, out TMP_Text statusSamples, out TMP_Text statusTimer, out TMP_Text statusComms);
            GeoScanner scanner = BuildToolStandAndScanner(root.transform);
            Transform rocksRoot = BuildRocks(root.transform);

            // ---------- Panels ----------
            Transform ui = Child(root.transform, "UI").transform;
            GameObject briefing = BuildBriefingBoard(ui, mission, out TMP_Text briefingComms, out TMP_Text briefingBody);
            GameObject outcome = BuildOutcomeBoard(ui, mission, out TMP_Text outcomeTitle, out TMP_Text outcomeBody);
            TMP_Text modeLabel = BuildLocomotionPanel(ui, locomotion);
            BuildCreditsBoard(ui);
            Transform wrist = BuildWristDisplay(out TMP_Text wristTimer, out TMP_Text wristSamples, out TMP_Text wristComms);

            // ---------- Wiring ----------
            Set(mission, "comms", comms);
            Set(mission, "storm", storm);
            Set(mission, "container", container);
            Set(mission, "scanner", scanner);
            Set(mission, "hud", hud);
            Set(mission, "rocksRoot", rocksRoot);

            Set(storm, "comms", comms);
            Set(storm, "sunLight", FindDirectionalLight());
            Set(storm, "stormRumble", rumble);

            Set(comms, "voiceSource", voice);
            SetList(comms, "subtitleTargets", briefingComms, statusComms, wristComms);

            Set(hud, "briefingRoot", briefing);
            Set(hud, "briefingBody", briefingBody);
            SetList(hud, "timerTexts", statusTimer, wristTimer);
            SetList(hud, "sampleTexts", statusSamples, wristSamples);
            Set(hud, "outcomeRoot", outcome);
            Set(hud, "outcomeTitle", outcomeTitle);
            Set(hud, "outcomeBody", outcomeBody);

            Set(locomotion, "modeLabel", modeLabel);

            Set(desktop, "locomotion", locomotion);
            Set(desktop, "wristDisplay", wrist);
            Set(desktop, "xriActions", FindXriActions());

            // Put the player at the start, facing the briefing board.
            XROrigin origin = FindInScene<XROrigin>();
            if (origin != null)
            {
                origin.transform.SetPositionAndRotation(Ground(PlayerStart), Quaternion.identity);
                if (origin.RequestedTrackingOriginMode != XROrigin.TrackingOriginMode.Floor)
                {
                    Debug.Log("[LunarSceneBuilder] Tip: set XR Origin > Tracking Origin Mode to 'Floor' (standing experience, see README).");
                }
            }
            else
            {
                Debug.LogWarning("[LunarSceneBuilder] No XR Origin in the scene - add 'XR Origin Hands (XR Rig)' from the VR template.");
            }
        }

        // ---------- Sample container ----------
        private static SampleContainer BuildContainer(Transform parent, out TMP_Text samplesText, out TMP_Text timerText, out TMP_Text commsText)
        {
            GameObject root = Child(parent, "Sample Container");
            root.transform.position = Ground(ContainerPos);

            Material white = LitMaterial("ContainerWhite", new Color(0.82f, 0.83f, 0.85f), 0.45f, 0.3f);
            Material dark = LitMaterial("ContainerDark", new Color(0.12f, 0.12f, 0.13f), 0.3f, 0.1f);
            Material gold = LitMaterial("ContainerGold", new Color(0.85f, 0.65f, 0.25f), 0.7f, 0.9f);

            Transform t = root.transform;
            Primitive(PrimitiveType.Cube, "Body", t, new Vector3(0, 0.3f, 0), new Vector3(1.0f, 0.6f, 0.6f), white);
            Primitive(PrimitiveType.Cube, "Wall Front", t, new Vector3(0, 0.75f, -0.28f), new Vector3(1.0f, 0.3f, 0.04f), white);
            Primitive(PrimitiveType.Cube, "Wall Back", t, new Vector3(0, 0.75f, 0.28f), new Vector3(1.0f, 0.3f, 0.04f), white);
            Primitive(PrimitiveType.Cube, "Wall Left", t, new Vector3(-0.48f, 0.75f, 0), new Vector3(0.04f, 0.3f, 0.6f), white);
            Primitive(PrimitiveType.Cube, "Wall Right", t, new Vector3(0.48f, 0.75f, 0), new Vector3(0.04f, 0.3f, 0.6f), white);
            Primitive(PrimitiveType.Cube, "Gold Trim", t, new Vector3(0, 0.62f, -0.305f), new Vector3(1.0f, 0.03f, 0.01f), gold);

            // Lid on a hinge along the back edge
            GameObject hinge = Child(t, "Lid Hinge");
            hinge.transform.localPosition = new Vector3(0, 0.9f, 0.3f);
            Primitive(PrimitiveType.Cube, "Lid", hinge.transform, new Vector3(0, 0.02f, -0.3f), new Vector3(1.04f, 0.04f, 0.64f), white);

            AudioSource sfx = root.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 1f;
            sfx.minDistance = 1f;
            sfx.maxDistance = 20f;

            var sockets = new XRSocketInteractor[3];
            var lights = new Renderer[3];
            float[] xs = { -0.3f, 0f, 0.3f };
            Material lightMat = LitMaterial("SlotLight", new Color(0.9f, 0.25f, 0.2f), 0.6f, 0f, emissive: true);
            for (int i = 0; i < 3; i++)
            {
                Primitive(PrimitiveType.Cylinder, $"Cradle {i + 1}", t, new Vector3(xs[i], 0.61f, 0), new Vector3(0.2f, 0.01f, 0.2f), dark);

                GameObject s = Child(t, $"Sample Socket {i + 1}");
                s.transform.localPosition = new Vector3(xs[i], 0.72f, 0f);
                var trigger = s.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 0.14f;
                XRSocketInteractor socket = s.AddComponent<XRSocketInteractor>();
                socket.showInteractableHoverMeshes = true;
                SampleSocketFilter filter = s.AddComponent<SampleSocketFilter>();
                Set(filter, "sfxSource", sfx);
                sockets[i] = socket;

                GameObject lamp = Primitive(PrimitiveType.Sphere, $"Slot Light {i + 1}", t, new Vector3(xs[i], 0.75f, -0.31f), Vector3.one * 0.04f, lightMat, keepCollider: false);
                lights[i] = lamp.GetComponent<Renderer>();
            }

            SampleContainer container = root.AddComponent<SampleContainer>();
            SetList(container, "sockets", sockets);
            SetList(container, "slotLights", lights);
            Set(container, "lidHinge", hinge.transform);
            Set(container, "sfxSource", sfx);

            // Status board above/behind the container
            RectTransform board = MakeCanvas("Status Board", t, new Vector3(0, 1.62f, 0.45f), Quaternion.identity, new Vector2(760, 360), 0.0011f);
            MakeText(board, "Header", "SAMPLE RETURN CONTAINER", 34, TextAlignmentOptions.Center, new Vector2(0, 0.82f), new Vector2(1, 1), FontStyles.Bold);
            samplesText = MakeText(board, "Samples", "SAMPLES  0 / 3", 58, TextAlignmentOptions.Center, new Vector2(0, 0.55f), new Vector2(1, 0.82f), FontStyles.Bold);
            timerText = MakeText(board, "Timer", "STORM ETA  5:00", 44, TextAlignmentOptions.Center, new Vector2(0, 0.35f), new Vector2(1, 0.55f));
            commsText = MakeText(board, "Comms", "", 24, TextAlignmentOptions.Center, new Vector2(0, 0f), new Vector2(1, 0.35f));
            commsText.color = new Color(0.75f, 0.9f, 1f);

            return container;
        }

        // ---------- Tool stand + scanner ----------
        private static GeoScanner BuildToolStandAndScanner(Transform parent)
        {
            GameObject stand = Child(parent, "Tool Stand");
            stand.transform.position = Ground(ToolStandPos);
            Primitive(PrimitiveType.Cube, "Table", stand.transform, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.9f, 0.4f),
                LitMaterial("ToolStand", new Color(0.55f, 0.56f, 0.58f), 0.35f, 0.5f));

            GameObject scannerGo = Child(parent, "Geo Scanner");
            scannerGo.transform.position = stand.transform.position + new Vector3(0, 1.03f, 0);
            Transform s = scannerGo.transform;

            Material body = LitMaterial("ScannerBody", new Color(0.92f, 0.93f, 0.95f), 0.5f, 0.2f);
            Material accent = LitMaterial("ScannerAccent", new Color(0.95f, 0.45f, 0.1f), 0.4f, 0.1f);
            Primitive(PrimitiveType.Cube, "Body", s, Vector3.zero, new Vector3(0.1f, 0.05f, 0.22f), body);
            GameObject grip = Primitive(PrimitiveType.Cube, "Grip", s, new Vector3(0, -0.06f, -0.05f), new Vector3(0.035f, 0.1f, 0.045f), accent);
            grip.transform.localRotation = Quaternion.Euler(15f, 0, 0);
            GameObject tip = Primitive(PrimitiveType.Cylinder, "Sensor Tip", s, new Vector3(0, 0, 0.12f), new Vector3(0.035f, 0.02f, 0.035f), accent);
            tip.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            GameObject emitter = Child(s, "Emitter");
            emitter.transform.localPosition = new Vector3(0, 0, 0.145f);

            GameObject attach = Child(s, "Attach (grip)");
            attach.transform.localPosition = new Vector3(0, -0.05f, -0.05f);

            Rigidbody rb = scannerGo.AddComponent<Rigidbody>();
            rb.mass = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            XRGrabInteractable grab = scannerGo.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attach.transform;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous; // steady screen while reading

            AudioSource sfx = scannerGo.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 1f;
            sfx.minDistance = 0.5f;
            sfx.maxDistance = 10f;

            var lr = scannerGo.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.004f;
            lr.sharedMaterial = UnlitMaterial("ScannerBeam", new Color(0.3f, 0.9f, 1f));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;

            // Screen on top of the scanner, tilted toward the user's eyes
            RectTransform screen = MakeCanvas("Screen", s, new Vector3(0, 0.027f, -0.01f), Quaternion.Euler(55f, 0, 0), new Vector2(300, 180), 0.0003f, new Color(0.02f, 0.05f, 0.06f, 1f));
            TMP_Text screenText = MakeText(screen, "Readout", "GEO-SCANNER", 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            screenText.color = new Color(0.55f, 1f, 0.85f);

            GeoScanner scanner = scannerGo.AddComponent<GeoScanner>();
            Set(scanner, "emitter", emitter.transform);
            Set(scanner, "screenText", screenText);
            Set(scanner, "beam", lr);
            Set(scanner, "sfxSource", sfx);
            return scanner;
        }

        // ---------- Rocks ----------
        private static Transform BuildRocks(Transform parent)
        {
            GameObject rocks = Child(parent, "Rocks");
            var meshes = new Mesh[4];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = SaveAsset(MakeRockMesh(1000 + i * 17), $"{GenRoot}/Meshes/LunarRock_{i}.asset");
            }
            Material rockMat = LitMaterial("LunarRock", new Color(0.3f, 0.3f, 0.3f), 0.1f, 0f, emissive: true);
            var physMat = new PhysicsMaterial("Regolith") { dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0.05f };
            physMat = SaveAsset(physMat, GenRoot + "/Materials/Regolith.asset");

            for (int i = 0; i < RockSpots.Length; i++)
            {
                var go = new GameObject($"Rock {i + 1:00}");
                go.transform.SetParent(rocks.transform, false);
                go.transform.position = Ground(new Vector3(RockSpots[i].x, 0, RockSpots[i].y)) + Vector3.up * 0.12f;
                go.transform.rotation = Quaternion.Euler(0, i * 47f, 0);
                go.transform.localScale = Vector3.one * (0.16f + (i % 3) * 0.03f);

                go.AddComponent<MeshFilter>().sharedMesh = meshes[i % meshes.Length];
                go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = meshes[i % meshes.Length];
                col.convex = true;
                col.sharedMaterial = physMat;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 2f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                XRGrabInteractable grab = go.AddComponent<XRGrabInteractable>();
                grab.useDynamicAttach = true;                                       // hold it where you grabbed it
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking; // collides while held (no clipping)

                go.AddComponent<RockSample>();
            }
            return rocks.transform;
        }

        // ---------- Panels ----------
        private static GameObject BuildBriefingBoard(Transform parent, MissionManager mission, out TMP_Text commsText, out TMP_Text bodyText)
        {
            RectTransform c = MakeCanvas("Briefing Board", parent, BriefingPos, FacePlayer(BriefingPos), new Vector2(980, 700), 0.0011f);
            MakeText(c, "Title", "MISSION BRIEFING - THE LUNAR GRAVITY SURVEY", 38, TextAlignmentOptions.Center, new Vector2(0, 0.88f), new Vector2(1, 1), FontStyles.Bold);
            bodyText = MakeText(c, "Body",
                "You are <b>Survey One</b>, a geologist on the Moon. A <color=#FFB070>solar storm</color> reaches this site in <b>{STORM}</b>.\n\n" +
                "<b>YOUR TASK</b>\n" +
                "1.  Take the <b>Geo-Scanner</b> from the tool stand ahead.\n" +
                "2.  Point it at rocks and pull the trigger to scan.\n" +
                "3.  Find <b>3 high-titanium basalt</b> samples (they glow once confirmed).\n" +
                "4.  Lock them into the <b>sample container</b> before the storm.\n\n" +
                "<size=80%>Why? Titanium-rich basalt contains ilmenite, which can be processed into oxygen for future lunar bases.\n" +
                "Moon gravity is 1/6 of Earth's - dropped rocks fall slowly.</size>\n\n" +
                "<size=75%><b>VR:</b> Grip = grab   Trigger = scan / press   Thumbstick = teleport &amp; turn\n" +
                "<b>Keyboard:</b> Mouse = look   WASD = walk   Left click = grab   Right click = scan   T = teleport</size>",
                24, TextAlignmentOptions.TopLeft, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.88f));
            commsText = MakeText(c, "Comms", "", 20, TextAlignmentOptions.Center, new Vector2(0.04f, 0.13f), new Vector2(0.96f, 0.2f));
            commsText.color = new Color(0.75f, 0.9f, 1f);
            MakeButton(c, "Begin Button", "BEGIN MISSION", new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.12f), mission.BeginMission, new Color(0.15f, 0.55f, 0.3f));
            return c.gameObject;
        }

        private static GameObject BuildOutcomeBoard(Transform parent, MissionManager mission, out TMP_Text title, out TMP_Text body)
        {
            RectTransform c = MakeCanvas("Outcome Board", parent, new Vector3(0, 1.5f, 3f), Quaternion.identity, new Vector2(820, 640), 0.0011f);
            title = MakeText(c, "Title", "MISSION COMPLETE", 52, TextAlignmentOptions.Center, new Vector2(0, 0.84f), new Vector2(1, 1), FontStyles.Bold);
            body = MakeText(c, "Body", "(summary)", 28, TextAlignmentOptions.TopLeft, new Vector2(0.08f, 0.2f), new Vector2(0.92f, 0.84f));
            MakeButton(c, "Restart Button", "RESTART", new Vector2(0.08f, 0.04f), new Vector2(0.48f, 0.16f), mission.RestartMission, new Color(0.2f, 0.45f, 0.75f));
            MakeButton(c, "Quit Button", "QUIT", new Vector2(0.52f, 0.04f), new Vector2(0.92f, 0.16f), mission.QuitApplication, new Color(0.45f, 0.2f, 0.2f));
            return c.gameObject; // MissionHUD hides it at start and moves it in front of the player at the end
        }

        private static TMP_Text BuildLocomotionPanel(Transform parent, LocomotionModeController locomotion)
        {
            RectTransform c = MakeCanvas("Movement Mode Panel", parent, LocoPanelPos, FacePlayer(LocoPanelPos), new Vector2(560, 360), 0.0011f);
            MakeText(c, "Title", "MOVEMENT MODE", 34, TextAlignmentOptions.Center, new Vector2(0, 0.8f), new Vector2(1, 1), FontStyles.Bold);
            TMP_Text label = MakeText(c, "Current", "CURRENT: COMFORT", 26, TextAlignmentOptions.Center, new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.8f));
            MakeButton(c, "Comfort Button", "COMFORT\n<size=70%>teleport</size>", new Vector2(0.05f, 0.08f), new Vector2(0.48f, 0.4f), locomotion.SetComfortMode, new Color(0.2f, 0.45f, 0.75f));
            MakeButton(c, "Lunar Button", "LUNAR\n<size=70%>walk + jump</size>", new Vector2(0.52f, 0.08f), new Vector2(0.95f, 0.4f), locomotion.SetLunarMode, new Color(0.55f, 0.35f, 0.15f));
            return label;
        }

        private static void BuildCreditsBoard(Transform parent)
        {
            RectTransform c = MakeCanvas("Credits Board", parent, CreditsPos, FacePlayer(CreditsPos), new Vector2(620, 820), 0.0011f);
            TMP_Text text = MakeText(c, "Credits", "CREDITS", 18, TextAlignmentOptions.TopLeft, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.97f));
            CreditsBoard board = c.gameObject.AddComponent<CreditsBoard>();
            Set(board, "creditsFile", AssetDatabase.LoadAssetAtPath<TextAsset>(CreditsPath));
            Set(board, "target", text);
        }

        private static Transform BuildWristDisplay(out TMP_Text timer, out TMP_Text samples, out TMP_Text comms)
        {
            timer = samples = comms = null;
            XROrigin origin = FindInScene<XROrigin>();
            Transform left = origin != null ? FindChild(origin.transform, "Left Controller") : null;
            if (left == null)
            {
                Debug.LogWarning("[LunarSceneBuilder] 'Left Controller' not found under the XR Origin - wrist display skipped.");
                return null;
            }

            Transform old = FindChild(left, "Wrist Display");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // PLACEHOLDER: tune position/rotation in Play Mode with the Simulator so it reads well
            // when the left hand is raised, then copy the values back here.
            RectTransform c = MakeCanvas("Wrist Display", left, new Vector3(0f, 0.05f, -0.07f), Quaternion.Euler(60f, 0f, 0f), new Vector2(340, 190), 0.0005f);
            timer = MakeText(c, "Timer", "STORM ETA 5:00", 40, TextAlignmentOptions.Center, new Vector2(0, 0.62f), new Vector2(1, 1), FontStyles.Bold);
            samples = MakeText(c, "Samples", "SAMPLES 0 / 3", 30, TextAlignmentOptions.Center, new Vector2(0, 0.38f), new Vector2(1, 0.62f));
            comms = MakeText(c, "Comms", "", 17, TextAlignmentOptions.Center, new Vector2(0.03f, 0f), new Vector2(0.97f, 0.38f));
            comms.color = new Color(0.75f, 0.9f, 1f);
            return c;
        }

        // ============================================================================ GENERATED ASSETS

        /// <summary>Low-poly rock: subdivided icosahedron with random dents, squashed, flat-shaded.</summary>
        private static Mesh MakeRockMesh(int seed)
        {
            var rnd = new System.Random(seed);
            float tau = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, tau, 0), new Vector3(1, tau, 0), new Vector3(-1, -tau, 0), new Vector3(1, -tau, 0),
                new Vector3(0, -1, tau), new Vector3(0, 1, tau), new Vector3(0, -1, -tau), new Vector3(0, 1, -tau),
                new Vector3(tau, 0, -1), new Vector3(tau, 0, 1), new Vector3(-tau, 0, -1), new Vector3(-tau, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            // One subdivision step (shared midpoints)
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var sub = new List<int>();
            for (int f = 0; f < faces.Count; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                sub.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            // Lumpy, squashed, flat-bottomed
            var scale = new Vector3(1f + (float)rnd.NextDouble() * 0.4f, 0.6f + (float)rnd.NextDouble() * 0.25f, 0.9f + (float)rnd.NextDouble() * 0.3f);
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * (1f + ((float)rnd.NextDouble() - 0.5f) * 0.35f);
                v = Vector3.Scale(v, scale);
                if (v.y < -0.3f) v.y = -0.3f + (v.y + 0.3f) * 0.3f;
                verts[i] = v * 0.5f;
            }

            // Flat shading: unique vertices per triangle; make every face point outward
            var outV = new List<Vector3>();
            var outT = new List<int>();
            for (int f = 0; f < sub.Count; f += 3)
            {
                Vector3 a = verts[sub[f]], b = verts[sub[f + 1]], c = verts[sub[f + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, (a + b + c) / 3f) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
                outT.Add(outV.Count); outV.Add(a);
                outT.Add(outV.Count); outV.Add(b);
                outT.Add(outV.Count); outV.Add(c);
            }

            var mesh = new Mesh { name = "LunarRock_" + seed };
            mesh.SetVertices(outV);
            mesh.SetTriangles(outT, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D MakeStarfield()
        {
            const int w = 4096, h = 2048;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color32[w * h];
            var rnd = new System.Random(42);
            for (int i = 0; i < 9000; i++)
            {
                int x = rnd.Next(w), y = rnd.Next(h);
                double r = rnd.NextDouble();
                byte b = (byte)(60 + 195 * r * r * r);  // mostly faint, a few bright
                byte tint = (byte)rnd.Next(0, 30);
                px[y * w + x] = new Color32((byte)Mathf.Max(0, b - tint), b, (byte)Mathf.Min(255, b + tint), 255);
                if (r > 0.97) // a handful of larger bright stars
                {
                    if (x + 1 < w) px[y * w + x + 1] = px[y * w + x];
                    if (y + 1 < h) px[(y + 1) * w + x] = px[y * w + x];
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexture(tex, GenRoot + "/Textures/Starfield.png", mipmaps: false, maxSize: 4096);
        }

        private static Texture2D MakeRegolithTexture()
        {
            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            var rnd = new System.Random(7);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // Tileable: sample noise on a torus-like wrap by using periodic coordinates
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float nx = Mathf.Cos(u) * 3f + 10f, ny = Mathf.Sin(u) * 3f + 10f;
                    float big = Mathf.PerlinNoise(nx + Mathf.Cos(v) * 3f, ny + Mathf.Sin(v) * 3f);
                    float fine = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                    float g = 0.34f + big * 0.12f + (fine - 0.5f) * 0.05f + ((float)rnd.NextDouble() - 0.5f) * 0.06f;
                    byte b = (byte)(Mathf.Clamp01(g) * 255f);
                    px[y * n + x] = new Color32(b, b, (byte)Mathf.Clamp(b + 2, 0, 255), 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexture(tex, GenRoot + "/Textures/Regolith.png", mipmaps: true, maxSize: 512);
        }

        // ============================================================================ HELPERS

        private static GameObject NewRoot(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(go, "Build " + name);
            return go;
        }

        private static void DestroyRoot(string name, Transform keep = null)
        {
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name != name) continue;
                if (keep != null && keep.IsChildOf(go.transform)) keep.SetParent(null, true); // rescue the terrain
                Undo.DestroyObjectImmediate(go);
            }
        }

        private static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Snaps a point to the terrain height (if there is a terrain).</summary>
        private static Vector3 Ground(Vector3 p)
        {
            Terrain t = FindInScene<Terrain>();
            if (t == null) return new Vector3(p.x, 0f, p.z);
            return new Vector3(p.x, t.SampleHeight(p) + t.transform.position.y, p.z);
        }

        private static Quaternion FacePlayer(Vector3 panelPos)
        {
            Vector3 dir = panelPos - PlayerStart;
            dir.y = 0f;
            return dir.sqrMagnitude < 0.001f ? Quaternion.identity : Quaternion.LookRotation(dir.normalized, Vector3.up);
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

        private static Light FindDirectionalLight()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional) return l;
                }
            }
            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static InputActionAsset FindXriActions()
        {
            InputActionManager manager = FindInScene<InputActionManager>();
            if (manager != null && manager.actionAssets != null && manager.actionAssets.Count > 0 && manager.actionAssets[0] != null)
            {
                return manager.actionAssets[0];
            }
            foreach (string guid in AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            Debug.LogWarning("[LunarSceneBuilder] Could not find 'XRI Default Input Actions'. Assign it on DesktopFallbackRig manually.");
            return null;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ---- Serialized-field wiring (the scripts keep their fields private) ----
        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[LunarSceneBuilder] {target.GetType().Name} has no field '{field}'."); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetList(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null || !p.isArray) { Debug.LogError($"[LunarSceneBuilder] {target.GetType().Name} has no list '{field}'."); return; }
            var clean = new List<Object>();
            foreach (Object v in values) if (v != null) clean.Add(v);
            p.arraySize = clean.Count;
            for (int i = 0; i < clean.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = clean[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- UI ----
        private static RectTransform MakeCanvas(string name, Transform parent, Vector3 localPos, Quaternion localRot, Vector2 sizePx, float metersPerPixel, Color? background = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            go.AddComponent<TrackedDeviceGraphicRaycaster>(); // lets XR rays (and the desktop fallback) press buttons

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPixel;
            rt.localPosition = localPos;
            rt.localRotation = localRot;

            var img = go.AddComponent<Image>();
            img.color = background ?? new Color(0.04f, 0.06f, 0.09f, 0.88f);
            return rt;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string text, float size, TextAlignmentOptions align,
            Vector2 anchorMin, Vector2 anchorMax, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(12, 8);
            rt.offsetMax = new Vector2(-12, -8);

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.fontStyle = style;
            t.color = new Color(0.92f, 0.95f, 1f);
            t.raycastTarget = false;
            return t;
        }

        private static UnityEngine.UI.Button MakeButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<UnityEngine.UI.Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = cb;
            UnityEventTools.AddPersistentListener(btn.onClick, onClick);

            TMP_Text t = MakeText(rt, "Label", label, 30, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, FontStyles.Bold);
            t.color = Color.white;
            return btn;
        }

        // ---- Materials / assets ----
        private static Material LitMaterial(string name, Color color, float smoothness, float metallic, bool emissive = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emissive)
            {
                // Enabling the keyword on the ASSET makes sure the emission shader variant is included in builds,
                // so scripts can turn the glow on at runtime.
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return SaveAsset(m, $"{GenRoot}/Materials/{name}.mat");
        }

        private static Material UnlitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.color = color;
            return SaveAsset(m, $"{GenRoot}/Materials/{name}.mat");
        }

        private static T SaveAsset<T>(T asset, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing); // keep the GUID so scene references survive
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Texture2D SaveTexture(Texture2D tex, string path, bool mipmaps, int maxSize)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.mipmapEnabled = mipmaps;
                importer.maxTextureSize = maxSize;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
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
    }
}
