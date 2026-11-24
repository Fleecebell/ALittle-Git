using System;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Extensions;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Unity.Pipeline.Timeline.Editor
{
    static class DirectorCommands
    {
        [CliCommand("configure_timeline_animation_track", "Edit exact native AnimationTrack root offset mode/pose and persist its Timeline asset. Explicit mode avoids guessing initial camera/actor placement; no scene sampling.",Tags=new[]{"rendering/timeline"})]
        public static object AnimationTrack([CliArg("track","Exact writable AnimationTrack asset/subasset.",Required=true)]ObjectRef track,
            [CliArg("offset_mode","Auto, ApplyTransformOffsets or ApplySceneOffsets.")]string offsetMode=null,
            [CliArg("position","Optional track translation offset [x,y,z].")]float[] position=null,
            [CliArg("rotation","Optional track Euler offset [x,y,z].")]float[] rotation=null)
        {
            UiAuthoring.GuardEditMode();var t=VisualAuthoring.Writable<AnimationTrack>(track);
            UiAuthoring.Vector(position,3,"position");UiAuthoring.Vector(rotation,3,"rotation");var mode=offsetMode==null?(TrackOffset?)null:VisualAuthoring.EnumValue<TrackOffset>(offsetMode);
            if(mode.HasValue)t.trackOffset=mode.Value;if(position!=null)t.position=new Vector3(position[0],position[1],position[2]);if(rotation!=null)t.eulerAngles=new Vector3(rotation[0],rotation[1],rotation[2]);
            EditorUtility.SetDirty(t);AssetDatabase.SaveAssetIfDirty(t);return new{handle=ObjectResolver.Describe(t),offsetMode=t.trackOffset.ToString(),position=new[]{t.position.x,t.position.y,t.position.z},rotation=new[]{t.eulerAngles.x,t.eulerAngles.y,t.eulerAngles.z}};
        }

        [CliCommand("configure_timeline_animation_clip", "Edit one exact native AnimationPlayableAsset's removeStartOffset, clip pose offsets and foot IK, then save its owning asset. removeStartOffset=true subtracts the source first-frame pose; Camera paths commonly require explicit false.",Tags=new[]{"rendering/timeline"})]
        public static object AnimationClip([CliArg("clip","Exact writable AnimationPlayableAsset from director clip query (not source .anim).",Required=true)]ObjectRef clip,
            [CliArg("remove_start_offset","Subtract the source clip's first frame root pose.")]bool? removeStartOffset=null,
            [CliArg("position","Optional clip offset [x,y,z].")]float[] position=null,
            [CliArg("rotation","Optional clip Euler offset [x,y,z].")]float[] rotation=null,
            [CliArg("foot_ik","Native humanoid foot IK switch.")]bool? footIk=null)
        {
            UiAuthoring.GuardEditMode();var c=VisualAuthoring.Writable<AnimationPlayableAsset>(clip);UiAuthoring.Vector(position,3,"position");UiAuthoring.Vector(rotation,3,"rotation");
            if(removeStartOffset.HasValue)c.removeStartOffset=removeStartOffset.Value;if(position!=null)c.position=new Vector3(position[0],position[1],position[2]);if(rotation!=null)c.eulerAngles=new Vector3(rotation[0],rotation[1],rotation[2]);if(footIk.HasValue)c.applyFootIK=footIk.Value;
            EditorUtility.SetDirty(c);AssetDatabase.SaveAssetIfDirty(c);return new{handle=ObjectResolver.Describe(c),c.removeStartOffset,c.applyFootIK,source=ObjectResolver.Describe(c.clip),position=new[]{c.position.x,c.position.y,c.position.z},rotation=new[]{c.eulerAngles.x,c.eulerAngles.y,c.eulerAngles.z}};
        }
        [CliCommand("configure_timeline_director", "Add/edit a native PlayableDirector on a loaded object, binding an explicit Timeline asset with Undo. Edit Mode only; does not start playback or save.",Tags=new[]{"rendering/timeline"})]
        public static object Configure([CliArg("target","Loaded GameObject/component.",Required=true)]ObjectRef target,
            [CliArg("timeline","TimelineAsset.",Required=true)]ObjectRef timeline,
            [CliArg("wrap_mode","Hold, Loop or None.")]string wrapMode=null,
            [CliArg("update_mode","GameTime, UnscaledGameTime, DSPClock or Manual.")]string updateMode=null,
            [CliArg("play_on_awake","Whether Play starts automatically.")]bool? playOnAwake=null)
        {
            UiAuthoring.GuardEditMode();var go=ExtensionObjects.Resolve(target);var asset=VisualAuthoring.Asset<TimelineAsset>(timeline);
            var wrap=wrapMode==null?(DirectorWrapMode?)null:VisualAuthoring.EnumValue<DirectorWrapMode>(wrapMode);
            var update=updateMode==null?(DirectorUpdateMode?)null:VisualAuthoring.EnumValue<DirectorUpdateMode>(updateMode);
            return UiAuthoring.Atomic<object>("Configure Timeline Director",()=>
            {
                var director=go.GetComponent<PlayableDirector>();if(director==null)director=Undo.AddComponent<PlayableDirector>(go);else Undo.RecordObject(director,"Configure Timeline Director");
                director.playableAsset=asset;if(wrap.HasValue)director.extrapolationMode=wrap.Value;if(update.HasValue)director.timeUpdateMode=update.Value;
                if(playOnAwake.HasValue)director.playOnAwake=playOnAwake.Value;ExtensionObjects.Changed(director);return Inspect(target);
            });
        }

        [CliCommand("bind_timeline_track", "Bind one exact Timeline TrackAsset handle to its required scene object type, or explicitly clear it. Validates ownership/output type before one Undo edit. Duplicate names are never guessed.",Tags=new[]{"rendering/timeline"})]
        public static object Bind([CliArg("director","Director object.",Required=true)]ObjectRef director,
            [CliArg("track","Exact TrackAsset ObjectRef from inspect_timeline_director.",Required=true)]ObjectRef track,
            [CliArg("target","Scene target GameObject/component. Omit only with clear=true.")]ObjectRef target=null,
            [CliArg("clear","Clear this binding.")]bool clear=false)
        {
            UiAuthoring.GuardEditMode();var d=ExtensionObjects.Require<PlayableDirector>(director);var t=VisualAuthoring.Asset<TrackAsset>(track);
            if(!(d.playableAsset is TimelineAsset timeline)||!timeline.GetOutputTracks().Contains(t))throw new ArgumentException("Track is not an output of this director's Timeline.");
            if(clear&&target!=null||!clear&&target==null)throw new ArgumentException("Supply target or clear=true.");
            UnityEngine.Object value=null;
            if(!clear)
            {
                var outputs=t.outputs.ToArray();if(outputs.Length!=1||outputs[0].outputTargetType==null)throw new ArgumentException("Track must expose one explicit output binding type.");
                var go=ExtensionObjects.Resolve(target);var expected=outputs[0].outputTargetType;
                if(!ObjectResolver.TryResolve(target,out var exact,out var error))throw new ArgumentException(error);
                if(expected.IsInstanceOfType(exact))value=exact;
                else if(exact is GameObject&&typeof(Component).IsAssignableFrom(expected))
                {
                    var matches=go.GetComponents(expected);
                    if(matches.Length>1)throw new ArgumentException("Multiple matching components; supply an exact component ObjectRef.");
                    value=matches.SingleOrDefault();
                }
                if(value==null||!expected.IsInstanceOfType(value))throw new ArgumentException("Track requires "+expected.FullName+"; no compatible target component found.");
            }
            return UiAuthoring.Atomic<object>("Bind Timeline Track",()=>
            {
                Undo.RecordObject(d,"Bind Timeline Track");if(clear)d.ClearGenericBinding(t);else d.SetGenericBinding(t,value);
                ExtensionObjects.Changed(d);return Inspect(director);
            });
        }

        [CliCommand("inspect_timeline_director", "Read actual Director time/state, Timeline tracks with exact handles, output binding types and scene bindings. Does not build/evaluate a graph.",Tags=new[]{"rendering/timeline"})]
        public static object Inspect([CliArg("target","Director object.",Required=true)]ObjectRef target)
        {
            var d=ExtensionObjects.Require<PlayableDirector>(target);var timeline=d.playableAsset as TimelineAsset;
            return new{handle=ObjectResolver.Describe(d),timeline=ObjectResolver.Describe(d.playableAsset),d.time,d.duration,state=d.state.ToString(),d.playOnAwake,
                wrapMode=d.extrapolationMode.ToString(),updateMode=d.timeUpdateMode.ToString(),graphValid=d.playableGraph.IsValid(),
                tracks=timeline==null?null:timeline.GetOutputTracks().Select(t=>new{handle=ObjectResolver.Describe(t),t.name,type=t.GetType().FullName,
                    outputs=t.outputs.Select(o=>o.outputTargetType?.FullName).ToArray(),binding=ObjectResolver.Describe(d.GetGenericBinding(t)),
                    animationOffset=t is AnimationTrack a?new{mode=a.trackOffset.ToString(),position=new[]{a.position.x,a.position.y,a.position.z},rotation=new[]{a.eulerAngles.x,a.eulerAngles.y,a.eulerAngles.z}}:null,
                    clips=t.GetClips().Select(c=>new{c.displayName,c.start,c.duration,asset=ObjectResolver.Describe(c.asset),
                        animation=c.asset is AnimationPlayableAsset p?new{p.removeStartOffset,p.applyFootIK,source=ObjectResolver.Describe(p.clip),position=new[]{p.position.x,p.position.y,p.position.z},rotation=new[]{p.eulerAngles.x,p.eulerAngles.y,p.eulerAngles.z}}:null}).ToArray()}).ToArray()};
        }

        [CliCommand("control_timeline_director", "Play/pause/stop/seek/evaluate a native Director in stable Play Mode only. Uses its native clock; seek evaluates at an explicit finite time. Not Undo; stop uses Timeline's native restoration semantics.",Tags=new[]{"rendering/timeline"})]
        public static object Control([CliArg("target","Director object.",Required=true)]ObjectRef target,
            [CliArg("action","play, pause, stop, seek or evaluate.",Required=true)]string action,
            [CliArg("time","Required for seek; seconds >=0, within finite Timeline duration.")]double? time=null)
        {
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("Director control requires stable Play Mode.");
            var d=ExtensionObjects.Require<PlayableDirector>(target);if(d.playableAsset==null)throw new ArgumentException("Director has no playable asset.");
            if(time.HasValue&&(double.IsNaN(time.Value)||double.IsInfinity(time.Value)||time<0))throw new ArgumentException("time must be finite and nonnegative.");
            if(action=="seek")
            {
                if(!time.HasValue||time>d.duration)throw new ArgumentException("seek requires time within Timeline duration.");
                d.time=time.Value;d.Evaluate();
            }
            else
            {
                if(time.HasValue)throw new ArgumentException("time is only valid for seek.");
                switch(action){case "play":d.Play();break;case "pause":d.Pause();break;case "stop":d.Stop();break;case "evaluate":d.Evaluate();break;default:throw new ArgumentException("Unknown action.");}
            }
            return Inspect(target);
        }
    }
}
