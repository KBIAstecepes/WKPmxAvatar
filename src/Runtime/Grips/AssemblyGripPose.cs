using System;
using UnityEngine;
namespace WKPmxAvatar {
 [Serializable] public sealed class AssemblyPartIdentity {
  public string Path,Mesh;public int Vertices,Submeshes;
 }
 // Desired visual item-root frame, with complete rigid-renderer identity.
 // Source child TRS/animation remain relative to this root; no gameplay objects move.
 public struct AssemblyGripPose {
  public string Item;public Vector3 Position,Scale;public Quaternion Rotation;
  public float Weight;public AssemblyPartIdentity[] Parts;
 }
}
