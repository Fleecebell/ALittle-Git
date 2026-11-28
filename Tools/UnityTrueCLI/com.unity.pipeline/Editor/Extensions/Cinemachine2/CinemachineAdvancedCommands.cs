using System;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;


namespace Unity.Pipeline.Cinemachine2.Editor
{
    static class CinemachineAdvancedCommands
    {
        [CliCommand("get_cinemachine_catalog", "Discover the installed Cinemachine component types, obsolete markers, and supported body/aim/noise choices. Real type metadata, not a hardcoded claim that every package version works.", Tags=new[]{"cinemachine/catalog"})]
        public static object Catalog() => new {
            version=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CinemachineBrain).Assembly)?.version,
            components=TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Where(type=>type.Assembly==typeof(CinemachineBrain).Assembly && type.IsPublic && !type.IsAbstract)
                .OrderBy(type=>type.FullName).Select(type=>new {name=type.FullName, pipeline=typeof(CinemachineComponentBase).IsAssignableFrom(type), extension=typeof(CinemachineExtension).IsAssignableFrom(type), obsolete=Attribute.IsDefined(type,typeof(ObsoleteAttribute))}).ToArray(),
            body=new[]{"follow","framing","third_person","orbital","dolly","hard_lock"}, aim=new[]{"composer","pov","hard_look","rotate_with_target"}, noise=new[]{"perlin"},
            note="Specific commands use typed version adapters; remaining components expose their actual serialized schema through inspect/configure_cinemachine_component."
        };

        private static CinemachineVirtualCamera Basic(ObjectRef target)=>ExtensionObjects.Require<CinemachineVirtualCamera>(target);
        private static CinemachineVirtualCameraBase Camera(ObjectRef target)=>ExtensionObjects.Require<CinemachineVirtualCameraBase>(target);
        private static T Part<T>(CinemachineVirtualCamera camera) where T:CinemachineComponentBase => camera.GetCinemachineComponent<T>();
        private static CinemachineComponentBase[] Parts(CinemachineVirtualCamera camera)=>camera.GetComponentPipeline()??Array.Empty<CinemachineComponentBase>();
        private static T Need<T>(ObjectRef target) where T:CinemachineComponentBase {
            var part=Part<T>(Basic(target)); if(part==null) throw new ArgumentException("Missing pipeline component "+typeof(T).Name+"; select it explicitly with set_cinemachine_pipeline."); return part;
        }
        private static CinemachineComponentBase Install(CinemachineVirtualCamera camera,string name) { switch(name) { case "follow":return camera.AddCinemachineComponent<CinemachineTransposer>(); case "framing":return camera.AddCinemachineComponent<CinemachineFramingTransposer>(); case "third_person":return camera.AddCinemachineComponent<Cinemachine3rdPersonFollow>(); case "orbital":return camera.AddCinemachineComponent<CinemachineOrbitalTransposer>(); case "dolly":return camera.AddCinemachineComponent<CinemachineTrackedDolly>(); case "hard_lock":return camera.AddCinemachineComponent<CinemachineHardLockToTarget>(); case "composer":return camera.AddCinemachineComponent<CinemachineComposer>(); case "pov":return camera.AddCinemachineComponent<CinemachinePOV>(); case "hard_look":return camera.AddCinemachineComponent<CinemachineHardLookAt>(); case "rotate_with_target":return camera.AddCinemachineComponent<CinemachineSameAsFollowTarget>(); case "perlin":return camera.AddCinemachineComponent<CinemachineBasicMultiChannelPerlin>(); default:throw new ArgumentException("Unknown pipeline choice: "+name); } }
        private static CinemachineCore.Stage Stage(string choice) {
            if(new[]{"follow","framing","third_person","orbital","dolly","hard_lock"}.Contains(choice)) return CinemachineCore.Stage.Body;
            if(new[]{"composer","pov","hard_look","rotate_with_target"}.Contains(choice)) return CinemachineCore.Stage.Aim;
            if(choice=="perlin") return CinemachineCore.Stage.Noise;
            throw new ArgumentException("Unknown pipeline choice: "+choice);
        }
        private static Type Kind(string choice) { switch(choice) { case "follow":return typeof(CinemachineTransposer); case "framing":return typeof(CinemachineFramingTransposer); case "third_person":return typeof(Cinemachine3rdPersonFollow); case "orbital":return typeof(CinemachineOrbitalTransposer); case "dolly":return typeof(CinemachineTrackedDolly); case "hard_lock":return typeof(CinemachineHardLockToTarget); case "composer":return typeof(CinemachineComposer); case "pov":return typeof(CinemachinePOV); case "hard_look":return typeof(CinemachineHardLookAt); case "rotate_with_target":return typeof(CinemachineSameAsFollowTarget); case "perlin":return typeof(CinemachineBasicMultiChannelPerlin); default: throw new ArgumentException("Unknown choice."); } }
        private static void Record(Component component) => Undo.RecordObject(component,"Configure Cinemachine");
        private static Vector3 V(float[] values,string name) { if(values==null||values.Length!=3)throw new ArgumentException(name+" requires [x,y,z]."); foreach(var value in values)ExtensionObjects.Finite(value,name);return new Vector3(values[0],values[1],values[2]); }
        private static float Nonnegative(float value,string name) { if(ExtensionObjects.Finite(value,name)<0)throw new ArgumentException(name+" must be nonnegative.");return value; }
        private static T Asset<T>(ObjectRef reference) where T:UnityEngine.Object {if(!ObjectResolver.TryResolve(reference,out var value,out var error)||!(value is T typed))throw new ArgumentException(error??"Wrong asset type.");return typed;}
        private static string NewAsset(string path) {var result=ProjectPaths.Resolve(path,out var error);if(result==null)throw new ArgumentException(error);if(!result.EndsWith(".asset",StringComparison.OrdinalIgnoreCase)||AssetDatabase.LoadMainAssetAtPath(result)!=null||System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),result)))throw new ArgumentException("Expected a NEW .asset path.");if(!AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(result).Replace('\\','/')))throw new ArgumentException("Containing folder must already exist.");return result;}

        [CliCommand("inspect_cinemachine_pipeline", "Read the actual body/aim/noise pipeline including CM2 hidden pipeline children, stage, validity and attached extensions.",Tags=new[]{"cinemachine/pipeline"})]
        public static object Pipeline([CliArg("target","Basic camera handle.",Required=true)]ObjectRef target) {
            var camera=Basic(target);return new{camera=ObjectResolver.Describe(camera),parts=Parts(camera).Where(part=>part!=null).Select(part=>new{handle=ObjectResolver.Describe(part),type=part.GetType().FullName,stage=part.Stage.ToString(),part.enabled,part.IsValid}).ToArray(),
                extensions=camera.GetComponents<CinemachineExtension>().Select(part=>new{handle=ObjectResolver.Describe(part),type=part.GetType().FullName,part.enabled}).ToArray()};
        }

        [CliCommand("set_cinemachine_pipeline", "Explicitly select body/aim/noise algorithms with Undo. Existing different algorithms require replace=true; no silent pipeline replacement.",Tags=new[]{"cinemachine/pipeline"})]
        public static object SetPipeline([CliArg("target","Basic camera.",Required=true)]ObjectRef target,
            [CliArg("body","follow, framing, third_person, orbital, dolly or hard_lock.")]string body=null,
            [CliArg("aim","composer, pov, hard_look or rotate_with_target.")]string aim=null,
            [CliArg("noise","perlin.")]string noise=null,
            [CliArg("replace","Allow replacement of existing stage algorithms.")]bool replace=false) {
            var camera=Basic(target);var choices=new[]{body,aim,noise}.Where(value=>value!=null).ToArray();
            if(choices.Length==0)throw new ArgumentException("Specify at least one stage.");
            foreach(var item in new[]{new{value=body,stage=CinemachineCore.Stage.Body},new{value=aim,stage=CinemachineCore.Stage.Aim},new{value=noise,stage=CinemachineCore.Stage.Noise}})if(item.value!=null&&Stage(item.value)!=item.stage)throw new ArgumentException("Choice is not valid for that stage.");
            foreach(var choice in choices) {var existing=Parts(camera).Where(part=>part!=null&&part.Stage==Stage(choice)).ToArray();if(existing.Any(part=>part.GetType()!=Kind(choice))&&!replace)throw new ArgumentException("Stage contains a different algorithm; use replace=true.");}
            using(new AuthoringUndoScope("Set Cinemachine Pipeline")) {
                foreach(var choice in choices) {var existing=Parts(camera).Where(part=>part!=null&&part.Stage==Stage(choice)).ToArray();if(existing.Length==1&&existing[0].GetType()==Kind(choice))continue;
                    foreach(var oldPart in existing)Undo.DestroyObjectImmediate(oldPart); camera.InvalidateComponentPipeline();
                    var added=Install(camera,choice);Undo.RegisterCreatedObjectUndo(added,"Set Cinemachine Pipeline");ExtensionObjects.Changed(added);}
                ExtensionObjects.Changed(camera);
            }return Pipeline(target);
        }

        [CliCommand("add_cinemachine_extension", "Add a selected Cinemachine behavior with Undo: collider, confiner2d, confiner3d, impulse_listener, camera_offset, group_framing. Version-unsupported choices are rejected explicitly.",Tags=new[]{"cinemachine/extensions"})]
        public static object AddExtension([CliArg("target","Camera object.",Required=true)]ObjectRef target,[CliArg("kind","Extension kind.",Required=true)]string kind) {
            var camera=Camera(target);Type type;switch(kind){ case "collider":type=typeof(CinemachineCollider);break;case "confiner3d":type=typeof(CinemachineConfiner);break;case "group_framing":throw new ArgumentException("CM2 group framing uses group_composer/framing body; select its actual pipeline component.");case "confiner2d":type=typeof(CinemachineConfiner2D);break;case "impulse_listener":type=typeof(CinemachineImpulseListener);break;case "camera_offset":type=typeof(CinemachineCameraOffset);break; default:throw new ArgumentException("Unsupported extension kind for this Cinemachine generation.");}
            var existing=camera.GetComponent(type);if(existing!=null)return ObjectResolver.Describe(existing);
            using(new AuthoringUndoScope("Add Cinemachine Extension")){var component=Undo.AddComponent(camera.gameObject,type);ExtensionObjects.Changed(component);return ObjectResolver.Describe(component);}
        }

        [CliCommand("inspect_cinemachine_component", "Read the actual serialized properties of a Cinemachine component. Supply its exact component handle; works with package components beyond the named presets.",Tags=new[]{"cinemachine/properties"})]
        public static object ComponentInfo([CliArg("target","Exact Cinemachine component handle.",Required=true)]ObjectRef target)=>ExtensionProperties.Read(ResolveComponent(target));
        [CliCommand("configure_cinemachine_component", "Write discovered serialized property paths with the core SerializedObject/Undo path. No guessed member names or script replacement; unsupported property kinds fail.",Tags=new[]{"cinemachine/properties"})]
        public static object ComponentSet([CliArg("target","Exact component handle.",Required=true)]ObjectRef target,[CliArg("properties","Property-path to JSON-value map.",Required=true)]JObject properties)=>ExtensionProperties.Write(ResolveComponent(target),properties);
        [CliCommand("remove_cinemachine_component", "Remove a pipeline component/extension with Undo. Camera/Brain/manager removal is refused. Explicit component identity is required.",Tags=new[]{"cinemachine/pipeline"})]
        public static object Remove([CliArg("target","Exact component handle.",Required=true)]ObjectRef target) {
            var component=ResolveComponent(target);if(!(component is CinemachineComponentBase)&&!(component is CinemachineExtension))throw new ArgumentException("Only pipeline components/extensions may be removed here.");
            var result=ObjectResolver.Describe(component);using(new AuthoringUndoScope("Remove Cinemachine Component")){Undo.DestroyObjectImmediate(component);}return result;
        }
        private static Component ResolveComponent(ObjectRef target) {
            if(!ObjectResolver.TryResolve(target,out var value,out var error)||!(value is Component component)||component.GetType().Assembly!=typeof(CinemachineBrain).Assembly)throw new ArgumentException(error??"Expected exact Cinemachine component identity, not a GameObject.");
            ExtensionObjects.Resolve(target);return component;
        }

        [CliCommand("configure_cinemachine_lens", "Set projection mode, clipping planes, roll and lens size with Undo. Brain may also require lens mode overrides enabled in CM3.",Tags=new[]{"cinemachine/lens"})]
        public static object Lens([CliArg("target","Basic camera.",Required=true)]ObjectRef target,
            [CliArg("projection","None, Perspective, Orthographic or Physical.")]string projection=null,
            [CliArg("near","Positive near plane.")]float? near=null,[CliArg("far","Far plane greater than near.")]float? far=null,
            [CliArg("roll","Dutch angle in degrees.")]float? roll=null,[CliArg("fov","Perspective FOV in (0,179). ")]float? fov=null,[CliArg("size","Positive orthographic size.")]float? size=null) {
            var camera=Basic(target);var lens=camera.m_Lens;LensSettings.OverrideModes mode=lens.ModeOverride;
            if(projection!=null&&(!Enum.TryParse(projection,true,out mode)||!Enum.IsDefined(typeof(LensSettings.OverrideModes),mode)))throw new ArgumentException("Invalid projection mode.");
            float n=near??lens.NearClipPlane,f=far??lens.FarClipPlane;if(ExtensionObjects.Finite(n,"near")<=0||ExtensionObjects.Finite(f,"far")<=n)throw new ArgumentException("Require 0 < near < far.");
            if(roll.HasValue)ExtensionObjects.Finite(roll.Value,"roll");if(fov.HasValue&&(ExtensionObjects.Finite(fov.Value,"fov")<=0||fov.Value>=179))throw new ArgumentException("Invalid FOV.");if(size.HasValue&&ExtensionObjects.Finite(size.Value,"size")<=0)throw new ArgumentException("Invalid size.");
            using(new AuthoringUndoScope("Configure Cinemachine Lens")){Record(camera);lens.ModeOverride=mode;lens.NearClipPlane=n;lens.FarClipPlane=f;if(roll.HasValue)lens.Dutch=roll.Value;if(fov.HasValue)lens.FieldOfView=fov.Value;if(size.HasValue)lens.OrthographicSize=size.Value;camera.m_Lens=lens;ExtensionObjects.Changed(camera);}return ExtensionProperties.Read(camera);
        }

        [CliCommand("configure_cinemachine_third_person", "Configure the selected third-person body: shoulder, arm, distance, side and damping. Requires third_person already selected.",Tags=new[]{"cinemachine/third_person"})]
        public static object ThirdPerson([CliArg("target","Basic camera.",Required=true)]ObjectRef target,
            [CliArg("distance","Positive camera distance.")]float? distance=null,[CliArg("shoulder","Shoulder [x,y,z].")]float[] shoulder=null,
            [CliArg("arm","Nonnegative vertical arm length.")]float? arm=null,[CliArg("side","Camera side 0..1.")]float? side=null,[CliArg("damping","Nonnegative [x,y,z].")]float[] damping=null) {
            var part=Need<Cinemachine3rdPersonFollow>(target);var s=shoulder==null?part.ShoulderOffset:V(shoulder,"shoulder");var d=damping==null?part.Damping:V(damping,"damping");
            if(distance.HasValue&&Nonnegative(distance.Value,"distance")==0)throw new ArgumentException("distance must be positive.");if(arm.HasValue)Nonnegative(arm.Value,"arm");if(side.HasValue&&(ExtensionObjects.Finite(side.Value,"side")<0||side.Value>1))throw new ArgumentException("side must be 0..1.");foreach(var value in new[]{d.x,d.y,d.z})Nonnegative(value,"damping");
            using(new AuthoringUndoScope("Configure Third Person")){Record(part);part.ShoulderOffset=s;part.Damping=d;if(distance.HasValue)part.CameraDistance=distance.Value;if(arm.HasValue)part.VerticalArmLength=arm.Value;if(side.HasValue)part.CameraSide=side.Value;ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }

        [CliCommand("configure_cinemachine_composition", "Configure aim composer screen point (0..1), dead zone and damping. CM3 centered coordinates are translated explicitly. Requires composer aim selected.",Tags=new[]{"cinemachine/composition"})]
        public static object Composition([CliArg("target","Basic camera.",Required=true)]ObjectRef target,
            [CliArg("screen","Normalized screen [x,y] in 0..1.")]float[] screen=null,[CliArg("dead_zone","Normalized [width,height] in 0..1.")]float[] deadZone=null,
            [CliArg("damping","Nonnegative [horizontal,vertical].")]float[] damping=null,[CliArg("offset","Target-space [x,y,z].")]float[] offset=null) {
            var part=Need<CinemachineComposer>(target);foreach(var pair in new[]{new{values=screen,name="screen"},new{values=deadZone,name="dead_zone"},new{values=damping,name="damping"}})if(pair.values!=null){if(pair.values.Length!=2)throw new ArgumentException(pair.name+" requires two values.");foreach(var value in pair.values){Nonnegative(value,pair.name);if(pair.name!="damping"&&value>1)throw new ArgumentException(pair.name+" must be <=1.");}}
            var targetOffset=offset==null?part.m_TrackedObjectOffset:V(offset,"offset");using(new AuthoringUndoScope("Configure Composition")){Record(part);if(screen!=null){part.m_ScreenX=screen[0];part.m_ScreenY=screen[1];}if(deadZone!=null){part.m_DeadZoneWidth=deadZone[0];part.m_DeadZoneHeight=deadZone[1];}if(damping!=null){part.m_HorizontalDamping=damping[0];part.m_VerticalDamping=damping[1];}part.m_TrackedObjectOffset=targetOffset; ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }

        [CliCommand("create_cinemachine_freelook", "Create a CM2 FreeLook or CM3 three-ring OrbitalFollow+RotationComposer camera. Axis values are manual until an input provider/controller is explicitly connected.",Tags=new[]{"cinemachine/orbit"})]
        public static object FreeLook([CliArg("name","New camera name.",Required=true)]string name,[CliArg("follow","Tracking target.",Required=true)]ObjectRef follow,[CliArg("look_at","Optional aim target; defaults to Follow.")]ObjectRef lookAt=null) {
            if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name is required.");var f=ExtensionObjects.Resolve(follow).transform;var l=lookAt==null?f:ExtensionObjects.Resolve(lookAt).transform;
            using(new AuthoringUndoScope("Create FreeLook")){var go=new GameObject(name);UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);Undo.RegisterCreatedObjectUndo(go,"Create FreeLook");var camera=Undo.AddComponent<CinemachineFreeLook>(go);camera.Follow=f;camera.LookAt=l;camera.m_XAxis.m_InputAxisName="";camera.m_YAxis.m_InputAxisName=""; return ObjectResolver.Describe(camera);}
        }
        [CliCommand("configure_cinemachine_orbit", "Set manual orbit axes and three-ring heights/radii. Vertical is normalized 0..1 across generations; horizontal is degrees. Does not install an input driver.",Tags=new[]{"cinemachine/orbit"})]
        public static object Orbit([CliArg("target","FreeLook/OrbitalFollow camera.",Required=true)]ObjectRef target,[CliArg("horizontal","Horizontal angle degrees.")]float? horizontal=null,
            [CliArg("vertical","Normalized vertical position 0..1.")]float? vertical=null,[CliArg("heights","Top/center/bottom heights.")]float[] heights=null,[CliArg("radii","Nonnegative top/center/bottom radii.")]float[] radii=null) {
            if(horizontal.HasValue)ExtensionObjects.Finite(horizontal.Value,"horizontal");if(vertical.HasValue&&(ExtensionObjects.Finite(vertical.Value,"vertical")<0||vertical.Value>1))throw new ArgumentException("vertical must be 0..1.");if(heights!=null)V(heights,"heights");if(radii!=null){V(radii,"radii");foreach(var radius in radii)Nonnegative(radius,"radii");}
            var part=ExtensionObjects.Require<CinemachineFreeLook>(target); using(new AuthoringUndoScope("Configure Orbit")){Record(part);if(horizontal.HasValue)part.m_XAxis.Value=horizontal.Value;if(vertical.HasValue)part.m_YAxis.Value=vertical.Value;for(int i=0;i<3;i++){if(heights!=null)part.m_Orbits[i].m_Height=heights[i];if(radii!=null)part.m_Orbits[i].m_Radius=radii[i];} ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }

        [CliCommand("create_cinemachine_target_group", "Create a weighted target group, used as Follow/LookAt by group framing cameras.",Tags=new[]{"cinemachine/groups"})]
        public static object CreateGroup([CliArg("name","New group name.",Required=true)]string name) {if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name is required.");using(new AuthoringUndoScope("Create Target Group")){var go=new GameObject(name);UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);Undo.RegisterCreatedObjectUndo(go,"Create Target Group");var group=Undo.AddComponent<CinemachineTargetGroup>(go);return ObjectResolver.Describe(group);}}
        [CliCommand("set_cinemachine_group_members", "Replace a group's explicit member list with Undo; resolve every member first. Weights/radii are nonnegative and duplicate identities are refused.",Tags=new[]{"cinemachine/groups"})]
        public static object GroupMembers([CliArg("target","Target group.",Required=true)]ObjectRef target,[CliArg("members","[{target:ObjectRef,weight:1,radius:0.5}], max 128.",Required=true)]GroupMember[] members) {
            var group=ExtensionObjects.Require<CinemachineTargetGroup>(target);if(members==null||members.Length>128)throw new ArgumentException("members must be 0..128 entries.");
            var resolved=members.Select(member=>{if(member==null)throw new ArgumentException("Null member.");Nonnegative(member.weight,"weight");Nonnegative(member.radius,"radius");return ExtensionObjects.Resolve(member.target).transform;}).ToArray();if(resolved.Distinct().Count()!=resolved.Length)throw new ArgumentException("Duplicate group member identity.");
            var targets=members.Select((member,i)=>new CinemachineTargetGroup.Target{target=resolved[i],weight=member.weight,radius=member.radius}).ToArray(); using(new AuthoringUndoScope("Set Target Group")){Record(group);group.m_Targets=targets;ExtensionObjects.Changed(group);}return ExtensionProperties.Read(group);
        }

        [CliCommand("configure_cinemachine_confiner", "Assign an existing 2D boundary or 3D volume and invalidate real caches. Requires the matching confiner extension already installed.",Tags=new[]{"cinemachine/confinement"})]
        public static object Confine([CliArg("target","Camera with confiner extension.",Required=true)]ObjectRef target,[CliArg("boundary","Collider2D or Collider object.",Required=true)]ObjectRef boundary,[CliArg("dimension","2d or 3d.")]string dimension="2d") {
            var go=Camera(target).gameObject;var shape=ExtensionObjects.Resolve(boundary);Component part;if(dimension=="2d"){part=go.GetComponent<CinemachineConfiner2D>();if(shape.GetComponent<Collider2D>()==null)throw new ArgumentException("No Collider2D.");}else if(dimension=="3d"){part=go.GetComponent<CinemachineConfiner>();if(shape.GetComponent<Collider>()==null)throw new ArgumentException("No Collider.");}else throw new ArgumentException("dimension must be 2d/3d.");if(part==null)throw new ArgumentException("Install matching confiner extension first.");
            using(new AuthoringUndoScope("Configure Confiner")){Record(part);if(part is CinemachineConfiner2D two){two.m_BoundingShape2D=shape.GetComponent<Collider2D>();two.InvalidateCache();}else{var three=(CinemachineConfiner)part;three.m_ConfineMode=CinemachineConfiner.Mode.Confine3D;three.m_BoundingVolume=shape.GetComponent<Collider>();three.InvalidatePathCache();}ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }
        [CliCommand("configure_cinemachine_obstacles", "Configure actual occlusion avoidance layers/radius/damping. Requires collider/deoccluder extension already installed.",Tags=new[]{"cinemachine/obstacles"})]
        public static object Obstacles([CliArg("target","Camera.",Required=true)]ObjectRef target,[CliArg("layers","Physics layer bit mask.")]int layers=1,[CliArg("radius","Nonnegative camera radius.")]float radius=.2f,[CliArg("damping","Nonnegative damping seconds.")]float damping=.2f) {
            Nonnegative(radius,"radius");Nonnegative(damping,"damping");var part=ExtensionObjects.Require<CinemachineCollider>(target);
            using(new AuthoringUndoScope("Configure Obstacles")){Record(part);part.m_CollideAgainst=layers;part.m_AvoidObstacles=true;part.m_CameraRadius=radius;part.m_Damping=damping; ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }

        [CliCommand("create_cinemachine_noise_profile", "Create a new NoiseSettings asset through the typed API. Not undoable; no overwrite. Configure amplitudes/frequencies on the camera separately.",Tags=new[]{"cinemachine/noise"})]
        public static object NoiseAsset([CliArg("path","New .asset path.",Required=true)]string path,[CliArg("frequency","Positive base frequency.")]float frequency=1,[CliArg("amplitude","Nonnegative orientation amplitude.")]float amplitude=1) {
            var destination=NewAsset(path);if(Nonnegative(frequency,"frequency")==0)throw new ArgumentException("frequency must be positive.");Nonnegative(amplitude,"amplitude");var asset=ScriptableObject.CreateInstance<NoiseSettings>();
            var axis=new NoiseSettings.NoiseParams{Amplitude=amplitude,Frequency=frequency};asset.OrientationNoise=new[]{new NoiseSettings.TransformNoiseParams{X=axis,Y=axis,Z=axis}};AssetDatabase.CreateAsset(asset,destination);AssetDatabase.SaveAssetIfDirty(asset);return ObjectResolver.Describe(asset);
        }
        [CliCommand("configure_cinemachine_noise", "Configure Perlin profile, amplitude and frequency with Undo. Requires perlin noise stage. ReSeed can be triggered explicitly.",Tags=new[]{"cinemachine/noise"})]
        public static object Noise([CliArg("target","Basic camera.",Required=true)]ObjectRef target,[CliArg("profile","NoiseSettings asset.")]ObjectRef profile=null,[CliArg("amplitude","Nonnegative gain.")]float? amplitude=null,[CliArg("frequency","Nonnegative gain.")]float? frequency=null,[CliArg("reseed","Randomize phase explicitly.")]bool reseed=false) {
            var part=Need<CinemachineBasicMultiChannelPerlin>(target);var noise=profile==null?null:Asset<NoiseSettings>(profile);if(amplitude.HasValue)Nonnegative(amplitude.Value,"amplitude");if(frequency.HasValue)Nonnegative(frequency.Value,"frequency");using(new AuthoringUndoScope("Configure Noise")){Record(part);if(profile!=null)part.m_NoiseProfile=noise;if(amplitude.HasValue)part.m_AmplitudeGain=amplitude.Value;if(frequency.HasValue)part.m_FrequencyGain=frequency.Value; if(reseed)part.ReSeed();ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }
        [CliCommand("create_cinemachine_impulse_source", "Create a uniform non-legacy impulse source with defined duration/channel. Listener extension must be explicitly installed on receiving cameras.",Tags=new[]{"cinemachine/impulse"})]
        public static object ImpulseSource([CliArg("name","Source object name.",Required=true)]string name,[CliArg("duration","Positive impulse duration.")]float duration=.3f,[CliArg("channel","Positive channel bit mask.")]int channel=1) {
            if(string.IsNullOrWhiteSpace(name)||Nonnegative(duration,"duration")==0||channel<=0)throw new ArgumentException("Valid name, duration and channel required.");using(new AuthoringUndoScope("Create Impulse Source")){var go=new GameObject(name);UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);Undo.RegisterCreatedObjectUndo(go,"Create Impulse Source");var source=Undo.AddComponent<CinemachineImpulseSource>(go);source.m_ImpulseDefinition.m_ImpulseType=CinemachineImpulseDefinition.ImpulseTypes.Uniform;source.m_ImpulseDefinition.m_ImpulseDuration=duration;source.m_ImpulseDefinition.m_ImpulseChannel=channel;source.m_ImpulseDefinition.m_ImpulseShape=CinemachineImpulseDefinition.ImpulseShapes.Bump; ExtensionObjects.Changed(source);return ObjectResolver.Describe(source);}
        }
        [CliCommand("emit_cinemachine_impulse", "Emit one real impulse in Play Mode. This is a live side effect, not Undoable; no retry when the result is unknown.",Tags=new[]{"cinemachine/impulse"})]
        public static object Emit([CliArg("target","Impulse source object.",Required=true)]ObjectRef target,[CliArg("velocity","Impulse vector [x,y,z].",Required=true)]float[] velocity) {
            if(!Application.isPlaying)throw new InvalidOperationException("Requires Play Mode.");var source=ExtensionObjects.Require<CinemachineImpulseSource>(target);var vector=V(velocity,"velocity");source.GenerateImpulseWithVelocity(vector);return new{source=ObjectResolver.Describe(source),frame=Time.frameCount,emitted=true};
        }

        [CliCommand("create_cinemachine_path", "Create a scene-local smooth path (CM2) or SplineContainer (CM3) from explicit local points. Undoable; no asset overwrite.",Tags=new[]{"cinemachine/dolly"})]
        public static object CreatePath([CliArg("name","New path object name.",Required=true)]string name,[CliArg("points","2..256 local [x,y,z] points.",Required=true)]float[][] points,[CliArg("loop","Close the path.")]bool loop=false) {
            if(string.IsNullOrWhiteSpace(name)||points==null||points.Length<2||points.Length>256)throw new ArgumentException("Provide a name and 2..256 points.");var vectors=points.Select(point=>V(point,"point")).ToArray();using(new AuthoringUndoScope("Create Dolly Path")){var go=new GameObject(name);UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);Undo.RegisterCreatedObjectUndo(go,"Create Dolly Path");var path=Undo.AddComponent<CinemachineSmoothPath>(go);path.m_Looped=loop;path.m_Waypoints=vectors.Select(point=>new CinemachineSmoothPath.Waypoint{position=point,roll=0}).ToArray();path.InvalidateDistanceCache(); ExtensionObjects.Changed(path);return ObjectResolver.Describe(path);}
        }
        [CliCommand("configure_cinemachine_dolly", "Assign a compatible path and normalized position 0..1 to the explicitly selected dolly body, with Undo.",Tags=new[]{"cinemachine/dolly"})]
        public static object Dolly([CliArg("target","Basic camera with dolly body.",Required=true)]ObjectRef target,[CliArg("path","CM2 path or CM3 SplineContainer object.",Required=true)]ObjectRef path,[CliArg("position","Normalized position 0..1.")]float position=0) {
            if(ExtensionObjects.Finite(position,"position")<0||position>1)throw new ArgumentException("position must be 0..1.");var part=Need<CinemachineTrackedDolly>(target);var curve=ExtensionObjects.Require<CinemachinePathBase>(path); using(new AuthoringUndoScope("Configure Dolly")){Record(part);part.m_Path=curve;part.m_PositionUnits=CinemachinePathBase.PositionUnits.Normalized;part.m_PathPosition=position; ExtensionObjects.Changed(part);}return ExtensionProperties.Read(part);
        }

        [CliCommand("create_cinemachine_manager", "Create a clear_shot, mixing, state_driven or sequence manager. Child cameras are explicitly parented with existing set_parent commands; no existing cameras are reparented automatically.",Tags=new[]{"cinemachine/managers"})]
        public static object Manager([CliArg("name","New manager object name.",Required=true)]string name,[CliArg("kind","clear_shot, mixing, state_driven or sequence.",Required=true)]string kind) {
            if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("name is required.");Type type;switch(kind){case "clear_shot":type=typeof(CinemachineClearShot);break;case "mixing":type=typeof(CinemachineMixingCamera);break;case "state_driven":type=typeof(CinemachineStateDrivenCamera);break;case "sequence":type=typeof(CinemachineBlendListCamera);break;default:throw new ArgumentException("Unknown manager kind.");}
            using(new AuthoringUndoScope("Create Camera Manager")){var go=new GameObject(name);UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);Undo.RegisterCreatedObjectUndo(go,"Create Camera Manager");return ObjectResolver.Describe(Undo.AddComponent(go,type));}
        }
        [CliCommand("set_cinemachine_sequence", "Replace a sequence manager's explicit camera/hold/blend instructions. Every referenced camera must be its direct child; all inputs validated before mutation.",Tags=new[]{"cinemachine/managers"})]
        public static object Sequence([CliArg("target","Sequence manager.",Required=true)]ObjectRef target,[CliArg("shots","[{camera:ObjectRef,hold:1,blend:0.5}], 1..128.",Required=true)]Shot[] shots,[CliArg("loop","Loop the sequence.")]bool loop=false) {
            var manager=ExtensionObjects.Require<CinemachineBlendListCamera>(target);if(shots==null||shots.Length<1||shots.Length>128)throw new ArgumentException("Require 1..128 shots.");var cameras=shots.Select(shot=>{if(shot==null)throw new ArgumentException("Null shot.");Nonnegative(shot.hold,"hold");Nonnegative(shot.blend,"blend");var camera=Camera(shot.camera);if(camera.transform.parent!=manager.transform)throw new ArgumentException("Shot camera must be a direct manager child.");return camera;}).ToArray();var instructions=shots.Select((shot,i)=>new CinemachineBlendListCamera.Instruction{m_VirtualCamera=cameras[i],m_Hold=shot.hold,m_Blend=new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut,shot.blend)}).ToArray();
            using(new AuthoringUndoScope("Configure Camera Sequence")){Record(manager);manager.m_Instructions=instructions;manager.m_Loop=loop; ExtensionObjects.Changed(manager);}return ExtensionProperties.Read(manager);
        }
        [CliCommand("set_cinemachine_mixer_weights", "Set all direct-child weights of a mixing camera (max 8) with Undo. Requires at least one positive weight.",Tags=new[]{"cinemachine/managers"})]
        public static object Mix([CliArg("target","Mixing camera.",Required=true)]ObjectRef target,[CliArg("weights","One nonnegative weight per direct child, max 8.",Required=true)]float[] weights) {
            var mixer=ExtensionObjects.Require<CinemachineMixingCamera>(target);var children=mixer.ChildCameras.ToArray();if(weights==null||weights.Length!=children.Length||weights.Length==0||weights.Length>8)throw new ArgumentException("Weight count must match 1..8 child cameras.");foreach(var weight in weights)Nonnegative(weight,"weight");if(!weights.Any(weight=>weight>0))throw new ArgumentException("At least one positive weight required.");using(new AuthoringUndoScope("Configure Camera Mixer")){Record(mixer);for(int i=0;i<weights.Length;i++)mixer.SetWeight(i,weights[i]);ExtensionObjects.Changed(mixer);}return new{children=children.Select((camera,i)=>new{camera=ObjectResolver.Describe(camera),weight=mixer.GetWeight(i)}).ToArray()};
        }

        [CliCommand("set_cinemachine_state_rules", "Bind an Animator and explicit full-path state hashes to child cameras with Undo. Does not guess state names. Hash existence must be checked against the intended Animator controller.",Tags=new[]{"cinemachine/managers"})]
        public static object StateRules([CliArg("target","State-driven manager.",Required=true)]ObjectRef target,[CliArg("animator","Animator object.",Required=true)]ObjectRef animator,
            [CliArg("rules","[{full_hash:int,camera:ObjectRef,activate_after:0,min_duration:0}], max 128.",Required=true)]StateRule[] rules,[CliArg("layer","Animator layer index.")]int layer=0) {
            var manager=ExtensionObjects.Require<CinemachineStateDrivenCamera>(target);var animation=ExtensionObjects.Require<Animator>(animator);
            if(animation.runtimeAnimatorController==null||layer<0||layer>=animation.layerCount)throw new ArgumentException("Animator controller/layer is unavailable.");if(rules==null||rules.Length>128)throw new ArgumentException("Require 0..128 rules.");
            var cameras=rules.Select(rule=>{if(rule==null)throw new ArgumentException("Null rule.");Nonnegative(rule.activate_after,"activate_after");Nonnegative(rule.min_duration,"min_duration");var camera=Camera(rule.camera);if(camera.transform.parent!=manager.transform)throw new ArgumentException("State camera must be a direct child.");return camera;}).ToArray();
            if(rules.Select(rule=>rule.full_hash).Distinct().Count()!=rules.Length)throw new ArgumentException("Duplicate state hash.");var instructions=rules.Select((rule,i)=>new CinemachineStateDrivenCamera.Instruction{m_FullHash=rule.full_hash,m_VirtualCamera=cameras[i],m_ActivateAfter=rule.activate_after,m_MinDuration=rule.min_duration}).ToArray();
            using(new AuthoringUndoScope("Set State-driven Camera Rules")){Record(manager);manager.m_AnimatedTarget=animation;manager.m_LayerIndex=layer;manager.m_Instructions=instructions; ExtensionObjects.Changed(manager);}return ExtensionProperties.Read(manager);
        }

        [CliCommand("set_cinemachine_custom_blends", "Create a NEW blend-table asset and bind it to the selected Brain. Preserves old tables; asset creation is not Undoable. Name-based Cinemachine blend rules are explicit.",Tags=new[]{"cinemachine/blends"})]
        public static object CustomBlends([CliArg("target","Brain object.",Required=true)]ObjectRef target,[CliArg("path","New blend-table .asset path.",Required=true)]string path,[CliArg("rules","[{from:string,to:string,duration:1,style:EaseInOut}], max 128.",Required=true)]BlendRule[] rules) {
            var brain=ExtensionObjects.Require<CinemachineBrain>(target);var destination=NewAsset(path);if(rules==null||rules.Length>128)throw new ArgumentException("Require 0..128 rules.");var definitions=rules.Select(rule=>{if(rule==null||string.IsNullOrWhiteSpace(rule.from)||string.IsNullOrWhiteSpace(rule.to))throw new ArgumentException("Each rule requires from/to names.");Nonnegative(rule.duration,"duration");if(!Enum.TryParse<CinemachineBlendDefinition.Style>(rule.style,true,out var style)||!Enum.IsDefined(typeof(CinemachineBlendDefinition.Style),style))throw new ArgumentException("Invalid blend style.");return new CinemachineBlendDefinition(style,rule.duration);}).ToArray();var table=ScriptableObject.CreateInstance<CinemachineBlenderSettings>();table.m_CustomBlends=rules.Select((rule,i)=>new CinemachineBlenderSettings.CustomBlend{m_From=rule.from,m_To=rule.to,m_Blend=definitions[i]}).ToArray();
            AssetDatabase.CreateAsset(table,destination);AssetDatabase.SaveAssetIfDirty(table);using(new AuthoringUndoScope("Bind Camera Blends")){Record(brain);brain.m_CustomBlends=table;ExtensionObjects.Changed(brain);}return ObjectResolver.Describe(table);
        }
        [CliCommand("prioritize_cinemachine_camera", "Move a camera to the top of its equal-priority queue in Play Mode. Does not increase its numerical priority or promise immediate blend completion.",Tags=new[]{"cinemachine/live"})]
        public static object Prioritize([CliArg("target","Camera.",Required=true)]ObjectRef target){if(!Application.isPlaying)throw new InvalidOperationException("Requires Play Mode.");var camera=Camera(target);camera.MoveToTopOfPrioritySubqueue();return ObjectResolver.Describe(camera);}
    }
    [Serializable] public sealed class GroupMember{public ObjectRef target;public float weight=1,radius=.5f;}
    [Serializable] public sealed class Shot{public ObjectRef camera;public float hold=1,blend=.5f;}
    [Serializable] public sealed class BlendRule{public string from,to,style="EaseInOut";public float duration=1;}
    [Serializable] public sealed class StateRule{public int full_hash;public ObjectRef camera;public float activate_after,min_duration;}
}
