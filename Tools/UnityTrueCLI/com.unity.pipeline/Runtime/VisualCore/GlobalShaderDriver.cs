using System;
using UnityEngine;

namespace Unity.Pipeline.VisualCore
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Pipeline Shader Globals")]
    public sealed class GlobalShaderDriver:MonoBehaviour
    {
        public GlobalShaderProfile profile;
        private GlobalShaderLease lease;
        public string State {get;private set;}="idle";
        public string[] Conflicts => lease==null?Array.Empty<string>():lease.Conflicts;
        private void OnEnable(){if(Application.isPlaying)Apply();}
        private void OnDisable()=>Restore();
        private void OnDestroy()=>Restore();
        public void Apply()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Driver applies only in Play Mode; use an explicit preview scope in Edit Mode.");
            if(!isActiveAndEnabled)throw new InvalidOperationException("Driver must be active and enabled before acquiring globals.");
            if(lease!=null&&lease.Active)throw new InvalidOperationException("Driver is already applied; restore it first.");
            if(profile==null)throw new InvalidOperationException("Global profile is required.");
            lease=new GlobalShaderLease();
            try{lease.Apply(profile.entries);State=lease.State;}
            catch{State="failed";throw;}
        }
        public void Restore(){if(lease!=null){lease.Restore();State=lease.State;}}
    }
}
