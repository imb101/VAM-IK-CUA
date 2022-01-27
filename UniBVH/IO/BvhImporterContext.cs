using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using MVR.FileManagementSecure;



namespace UniHumanoid
{
    [Obsolete("use BvhImporterContext")]
    public class ImporterContext: BvhImporterContext
    {
    }

    public class BvhImporterContext
    {
        #region Source
        String m_path;
        public String Path
        {
            get { return m_path; }
            set
            {
                if (m_path == value) return;
                m_path = value;
            }
        }
        public String Source; // source
        public Bvh Bvh;
        #endregion

        #region Imported
        public GameObject Root;
        public List<Transform> Nodes = new List<Transform>();
        public AnimationClip Animation;
        public AvatarDescription AvatarDescription;
        public Avatar Avatar;
        public Mesh Mesh;
        public Material Material;
        #endregion

        #region Load
        [Obsolete("use Load(path)")]
        public void Parse()
        {
            Parse(Path);
        }

        public void Parse(string path)
        {
            Path = path;
            Source = FileManagerSecure.ReadAllText(Path);//, Encoding.UTF8);
            Bvh = Bvh.Parse(Source);
        }

        public void Load()
        {
            //
            // build hierarchy
            //

            Root = new GameObject(FileManagerSecure.GetFileName(Path));
//            Root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(Path));
            var hips = BuildHierarchy(Root.transform, Bvh.Root, 1.0f);
            var skeleton = Skeleton.Estimate(hips);
            var description = AvatarDescription.Create(hips.Traverse().ToArray(), skeleton);

            //
            // scaling. reposition
            //
            float scaling = 1.0f;
            {
                //var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var foot = hips.Traverse().Skip(skeleton.GetBoneIndex(HumanBodyBones.LeftFoot)).First();
                var hipHeight = hips.position.y - foot.position.y;
                // hips height to a meter
                scaling = 1.0f / hipHeight;
                foreach (var x in Root.transform.Traverse())
                {
                    x.localPosition *= scaling;
                }

                var scaledHeight = hipHeight * scaling;
                hips.position = new Vector3(0, scaledHeight, 0); // foot to ground
            }

            //
            // avatar
            //
            Avatar = description.CreateAvatar(Root.transform);
            Avatar.name = "Avatar";
            AvatarDescription = description;
            var animator = Root.AddComponent<Animator>();
            animator.avatar = Avatar;

            //
            // create AnimationClip
            //
            Animation = BvhAnimation.CreateAnimationClip(Bvh, scaling);
            Animation.name = Root.name;
            Animation.legacy = true;
            Animation.wrapMode = WrapMode.Loop;

            var animation = Root.AddComponent<Animation>();
            animation.AddClip(Animation, Animation.name);
            animation.clip = Animation;
            animation.Play();

            var humanPoseTransfer = Root.AddComponent<HumanPoseTransfer>();
            humanPoseTransfer.Avatar = Avatar;

            // create SkinnedMesh for bone visualize
            var renderer = SkeletonMeshUtility.CreateRenderer(animator);
            Material = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = Material;
            Mesh = renderer.sharedMesh;
            Mesh.name = "box-man";

            Root.AddComponent<BoneMapping>();

        }

        static Transform BuildHierarchy(Transform parent, BvhNode node, float toMeter)
        {
            var go = new GameObject(node.Name);
            go.transform.localPosition = node.Offset.ToXReversedVector3() * toMeter;
            go.transform.SetParent(parent, false);

            //var gizmo = go.AddComponent<BoneGizmoDrawer>();
            //gizmo.Draw = true;

            foreach (var child in node.Children)
            {
                BuildHierarchy(go.transform, child, toMeter);
            }

            return go.transform;
        }
        #endregion


        public void Destroy(bool destroySubAssets)
        {
            if (Root != null) GameObject.DestroyImmediate(Root);
            if (destroySubAssets)
            {

            }
        }
    }
}
