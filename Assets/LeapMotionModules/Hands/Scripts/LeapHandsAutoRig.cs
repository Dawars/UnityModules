﻿using UnityEngine;
using System.Collections;

namespace Leap.Unity {
  [ExecuteInEditMode]
  public class LeapHandsAutoRig : MonoBehaviour {
    public Animator AnimatorForMapping;
    public HandPool HandPoolToPopulate;
    public RiggedHand RiggedHand_L;
    public RiggedHand RiggedHand_R;
    public HandTransitionBehavior HandTransitionBehavior_L;
    public HandTransitionBehavior HandTransitionBehavior_R;

    public RiggedFinger RiggedFinger_L_Thumb;
    public RiggedFinger RiggedFinger_L_Index;
    public RiggedFinger RiggedFinger_L_Mid;
    public RiggedFinger RiggedFinger_L_Ring;
    public RiggedFinger RiggedFinger_L_Pinky;
    public RiggedFinger RiggedFinger_R_Thumb;
    public RiggedFinger RiggedFinger_R_Index;
    public RiggedFinger RiggedFinger_R_Mid;
    public RiggedFinger RiggedFinger_R_Ring;
    public RiggedFinger RiggedFinger_R_Pinky;

    public string ModelGroupName = "RiggedHands";


    public bool UseMetaCarpals;

    // Use this for initialization
    void Start() {

    }

    [ContextMenu("AutoRig")]
    void AutoRig() {
      //Assigning these here since this component gets added and used at editor time
      AnimatorForMapping = gameObject.GetComponent<Animator>();
      HandPoolToPopulate = GameObject.FindObjectOfType<HandPool>();
      Reset();

      //Find hands and assign RiggedHands
      Transform Hand_L = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftHand);
      RiggedHand_L = Hand_L.gameObject.AddComponent<RiggedHand>();
      HandTransitionBehavior_L =Hand_L.gameObject.AddComponent<HandEnableDisable>();
      RiggedHand_L.Handedness = Chirality.Left;
      RiggedHand_L.SetEditorLeapPose = false;

      Transform Hand_R = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightHand);
      RiggedHand_R = Hand_R.gameObject.AddComponent<RiggedHand>();
      HandTransitionBehavior_R = Hand_R.gameObject.AddComponent<HandEnableDisable>();
      RiggedHand_R.Handedness = Chirality.Right;
      RiggedHand_R.SetEditorLeapPose = false;

      //Find palms and assign to RiggedHands
      RiggedHand_L.palm = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftHand);
      RiggedHand_R.palm = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightHand);

      //Find Fingers and assign RiggedFingers
      RiggedFinger_L_Thumb = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftThumbProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_L_Thumb.fingerType = Finger.FingerType.TYPE_THUMB;
      RiggedFinger_L_Index = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftIndexProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_L_Index.fingerType = Finger.FingerType.TYPE_INDEX;
      RiggedFinger_L_Mid = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftMiddleProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_L_Mid.fingerType = Finger.FingerType.TYPE_MIDDLE;
      RiggedFinger_L_Ring = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftRingProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_L_Ring.fingerType = Finger.FingerType.TYPE_RING;
      RiggedFinger_L_Pinky = AnimatorForMapping.GetBoneTransform(HumanBodyBones.LeftLittleProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_L_Pinky.fingerType = Finger.FingerType.TYPE_PINKY;
      RiggedFinger_R_Thumb = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightThumbProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_R_Thumb.fingerType = Finger.FingerType.TYPE_THUMB;
      RiggedFinger_R_Index = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightIndexProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_R_Index.fingerType = Finger.FingerType.TYPE_INDEX;
      RiggedFinger_R_Mid = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightMiddleProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_R_Mid.fingerType = Finger.FingerType.TYPE_MIDDLE;
      RiggedFinger_R_Ring = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightRingProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_R_Ring.fingerType = Finger.FingerType.TYPE_RING;
      RiggedFinger_R_Pinky = AnimatorForMapping.GetBoneTransform(HumanBodyBones.RightLittleProximal).gameObject.AddComponent<RiggedFinger>();
      RiggedFinger_R_Pinky.fingerType = Finger.FingerType.TYPE_PINKY;
      //Trigger SetupRiggedHand in RiggedHands

      RiggedHand_L.AutoRigRiggedHand(RiggedHand_L.palm, RiggedFinger_L_Pinky.transform, RiggedFinger_L_Index.transform);
      RiggedHand_R.AutoRigRiggedHand(RiggedHand_R.palm, RiggedFinger_R_Pinky.transform, RiggedFinger_R_Index.transform);
      ModelGroupName = transform.name;
      HandPoolToPopulate.AddNewGroup(ModelGroupName, RiggedHand_L, RiggedHand_R);
    }


    void Reset() {
      RiggedFinger[] riggedFingers = GetComponentsInChildren<RiggedFinger>();
      foreach (RiggedFinger finger in riggedFingers) {
        DestroyImmediate(finger);
      }
      DestroyImmediate(RiggedHand_L);
      DestroyImmediate(RiggedHand_R);
      DestroyImmediate(HandTransitionBehavior_L);
      DestroyImmediate(HandTransitionBehavior_R);
      if (HandPoolToPopulate != null) {
        HandPoolToPopulate.RemoveGroup(ModelGroupName);
      }
    }
    void OnDestroy() {
      if (HandPoolToPopulate != null) {
        HandPoolToPopulate.RemoveGroup(ModelGroupName);
      }
    }

    // Update is called once per frame
    void Update() {

    }
  }
}