using UnityEngine;

// Reusable runtime component; no UnityEditor dependency or changes to the installed plugin.
// Set MaterialIndex to the face submesh. Other players/materials keep their own property blocks.
[DefaultExecutionOrder(10000)]
public sealed class FaceFrameBinding : MonoBehaviour
{
    public Renderer Target;
    public Transform Head;
    public int MaterialIndex;
    public int[] AdditionalMaterialIndices;
    public Vector3 LocalForward = Vector3.back;
    public Vector3 LocalUp = Vector3.up;
    MaterialPropertyBlock block;
    static readonly int EnabledId=Shader.PropertyToID("_HeadFrameEnabled");
    static readonly int RightId=Shader.PropertyToID("_HeadRight");
    static readonly int UpId=Shader.PropertyToID("_HeadUp");
    static readonly int ForwardId=Shader.PropertyToID("_HeadForward");

    public bool Refresh()
    {
        if(Target==null || Head==null || MaterialIndex<0) return false;
        Vector3 forward=Head.rotation*LocalForward, up=Head.rotation*LocalUp;
        if(forward.sqrMagnitude<1e-8f || up.sqrMagnitude<1e-8f) return false;
        forward.Normalize(); up-=forward*Vector3.Dot(up,forward);
        if(up.sqrMagnitude<1e-8f) return false;
        up.Normalize(); Vector3 right=Vector3.Cross(forward,up).normalized;
        if(block==null) block=new MaterialPropertyBlock();
        Write(MaterialIndex,right,up,forward);
        if(AdditionalMaterialIndices!=null)foreach(int index in AdditionalMaterialIndices)if(index>=0&&index!=MaterialIndex)Write(index,right,up,forward);
        return true;
    }
    void Write(int index,Vector3 right,Vector3 up,Vector3 forward)
    {
        Target.GetPropertyBlock(block,index);block.SetVector(RightId,right);block.SetVector(UpId,up);block.SetVector(ForwardId,forward);
        block.SetFloat(EnabledId,1);Target.SetPropertyBlock(block,index);
    }
    void LateUpdate() { Refresh(); }
    public void Clear()
    {
        if(Target==null || block==null || MaterialIndex<0) return;
        Target.GetPropertyBlock(block,MaterialIndex); block.SetFloat(EnabledId,0);
        Target.SetPropertyBlock(block,MaterialIndex);
        if(AdditionalMaterialIndices!=null)foreach(int index in AdditionalMaterialIndices)
            if(index>=0&&index!=MaterialIndex){Target.GetPropertyBlock(block,index);block.SetFloat(EnabledId,0);Target.SetPropertyBlock(block,index);}
    }
    void OnDisable() { Clear(); }
}
