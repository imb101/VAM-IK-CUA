using System;
using System.Collections.Generic;
using System.Linq;
using RootMotion.FinalIK;
using UnityEngine;
using Blazedust.CUAM;
using System.Collections;
using UnityEngine.UI;
using SimpleJSON;

namespace IKCUA
{
	enum SolverType
	{
		CCD,
		FABBRIK,
		Limb,
		FullBody
	}

	class VAMIKSolver
	{
		public IK solver;
		public Dictionary<Transform, Atom> effectors;
		public Animator animator;
		public FBBIKHeadEffector headEffector;
		public Atom headEffectorAtom;
		public JSONStorableBool forceEnable;
		public bool newlyAddedSolver = true;

		public VAMIKSolver(IK solver_)
		{
			solver = solver_;
			effectors = new Dictionary<Transform, Atom>();
		}

		public void addEffector(Atom eff, Transform bone)
		{
			effectors.Add(bone,eff);
		}
	}

	class TransformSettings
	{
		public Quaternion localRotation;
		public Vector3 localPosition;
		public Transform origTransform;

		public TransformSettings(Transform t)
		{
			localPosition = t.localPosition;
			localRotation = t.localRotation;
			origTransform = t;
		}
	}

	class VamIK : MVRScript
	{
		JSONStorableStringChooser ikSolverJSON;
		JSONStorableString ccdSolvers;
		JSONStorableString limbSolvers;
		JSONStorableString fbbSolver;
		JSONStorableString fabbrikSolvers;

		Dictionary<string, List<GameObject>> markedBones;
		Dictionary<string, LineRenderer> LineRendererMap;

		JSONStorableFloat debugWidth;
		JSONStorableStringChooser debugColor;

		Dictionary<string, Transform> transforms;

		List<object[]> originalMeshArmature;

		Dictionary<int, List<UIDynamic>> uiDict;
		Dictionary<int, VAMIKSolver> solverDict;
		int counter = 0;
		List<string> transformIds;
		Atomizer atomizer;
		bool issubsceneloading = true;
		bool subscene = false;
		bool attachedXPSLoader = false;
		string XPSLoaderPluginID = null;
		JSONClass pluginJson;
		bool checkSolvers = false;
		const string XPSLoaderName = "XPSLoader.XPSLoader";

		List<string> solverChoicesFull = new List<String>(new String[] { SolverType.CCD.ToString(), SolverType.FABBRIK.ToString(), SolverType.Limb.ToString(), SolverType.FullBody.ToString() });
		List<string> solverChoicesMinusFBB = new List<String>(new String[] { SolverType.CCD.ToString(), SolverType.FABBRIK.ToString(), SolverType.Limb.ToString() });

		private void SubSceneRefresh()
		{			
			refreshTransforms(true);
			issubsceneloading = false;
		}

		private void checkForOtherPlugins()
		{
			foreach (string st in this.containingAtom.GetStorableIDs())
			{
				if (st.Contains(XPSLoaderName))
				{
					XPSLoaderPluginID = st;
					attachedXPSLoader = true;
				}
			}

		}

		private IEnumerator restoreScene()
		{
			//do we have an XPS loader ?
			checkForOtherPlugins();

			CustomUnityAssetLoader dd = (CustomUnityAssetLoader)containingAtom.GetStorableByID("asset");

			while (SuperController.singleton.isLoading)
			{
				yield return null;
			}

			//no XPS loader and we have a custom asset.
			if (!attachedXPSLoader && dd != null)
			{
				string assetUrl = dd.GetUrlParamValue("assetUrl");
				string assetName = dd.GetStringParamValue("assetName");

				if (assetUrl != null && assetName != null)
				{
					while (!dd.isAssetLoaded)
					{
						yield return null;
					}
				}
			}
			else if (attachedXPSLoader)
			{
				JSONStorable xpsL = this.containingAtom.GetStorableByID(XPSLoaderPluginID);
				var bindings = new List<object>();
				if (xpsL != null)
				{
					xpsL.SendMessage("ModelLoadComplete", bindings, SendMessageOptions.DontRequireReceiver);
					bool modlLoad = (bool)bindings[0];

					while (!modlLoad)
					{
						xpsL.SendMessage("ModelLoadComplete", bindings, SendMessageOptions.RequireReceiver);
						modlLoad = (bool)bindings[0];
						yield return null;
					}
				}
			}

			if (subscene)
				SubSceneRefresh();

			recordOriginalTransforms();

			//CCD Solvers
			string[] ccdSolvParam = ccdSolvers.val.Split('|');

			foreach (string ccdSolvInd in ccdSolvParam)
			{
				if (ccdSolvInd.Contains("="))
					restoreSolver(SolverType.CCD, ccdSolvInd);
				else
					continue;
			}			
			
			string[] fabbrikSolvParam = fabbrikSolvers.val.Split('|');
			foreach (string fabbSolvInd in fabbrikSolvParam)
			{
				if (fabbSolvInd.Contains("="))
					restoreSolver(SolverType.FABBRIK, fabbSolvInd);
				else
					continue;
			}


			string[] limbSolvParam = limbSolvers.val.Split('|');
			foreach (string limbSolvParamInd in limbSolvParam)
			{
				if (limbSolvParamInd.Contains("="))
					restoreSolver(SolverType.Limb, limbSolvParamInd);
				else
					continue;
			}

			if (!fbbSolver.val.Trim().Equals(""))
			{
				restoreSolver(SolverType.FullBody, fbbSolver.val);
			}

			StartCoroutine(enableIKSolvers());

		}

		public GameObject getActualContainingGOM(bool forceSS = false)
		{
			if (this.containingAtom.isSubSceneRestore || subscene || forceSS)
			{				
				return this.containingAtom.containingSubScene.containingAtom.gameObject;
			}
			else
			{
				return containingAtom.gameObject;
			}
		}

		public Atom getActualContainingAtom(bool forceSS = false)
		{
			if (this.containingAtom.isSubSceneRestore || subscene || forceSS)
				return SuperController.singleton.GetAtomByUid(this.containingAtom.subScenePath.Split('/')[0]);
			else
				return containingAtom;
		}

		protected Transform CreateUIElement(Transform prefab, RectTransform panel)
		{
			Transform transform = null;
			if (prefab != null)
			{
				transform = UnityEngine.Object.Instantiate(prefab);
				bool flag = false;

				if (panel != null)
				{
					flag = true;
					transform.SetParent(panel, worldPositionStays: false);
				}


				if (flag)
				{
					transform.gameObject.SetActive(value: true);
				}
				else
				{
					transform.gameObject.SetActive(value: false);
				}

			}
			return transform;
		}

		public UIDynamicPopup CreateFilterablePopup(JSONStorableStringChooser jsc, RectTransform panel)
		{
			UIDynamicPopup uIDynamicPopup = null;
			if (manager != null && manager.configurableFilterablePopupPrefab != null && jsc.popup == null)
			{
				Transform transform = CreateUIElement(manager.configurableFilterablePopupPrefab.transform, panel);
				if (transform != null)
				{
					uIDynamicPopup = transform.GetComponent<UIDynamicPopup>();
					if (uIDynamicPopup != null)
					{
						popupToJSONStorableStringChooser.Add(uIDynamicPopup, jsc);
						uIDynamicPopup.label = jsc.name;
						jsc.popup = uIDynamicPopup.popup;
					}
				}
			}
			return uIDynamicPopup;
		}

		public UIDynamicButton CreateButton(string label, RectTransform panel)
		{
			UIDynamicButton uIDynamicButton = null;
			if (manager != null && manager.configurableButtonPrefab != null)
			{
				Transform transform = CreateUIElement(manager.configurableButtonPrefab.transform, panel);
				if (transform != null)
				{
					uIDynamicButton = transform.GetComponent<UIDynamicButton>();
					if (uIDynamicButton != null)
					{
						uIDynamicButton.label = label;
					}
				}
			}
			return uIDynamicButton;
		}

		public static Color ToColor(string color)
		{
			Color retCol;
			ColorUtility.TryParseHtmlString(color, out retCol);
			return retCol;
		}

		protected GameObject markTransform(Transform target)
		{
			GameObject gom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			Destroy(gom.GetComponent<Collider>());
			gom.name = "marker_" + target.name;
			gom.transform.position = target.position;
			gom.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
			MeshRenderer mr = gom.GetComponent<MeshRenderer>();
			mr.material = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
			mr.material.color = ToColor(debugColor.val);
			gom.transform.parent = target;

			return gom;
		}

		public void SetDebugColor(Color col)
		{
			foreach (KeyValuePair<string, LineRenderer> lrm in LineRendererMap)
			{
				LineRenderer lr = lrm.Value;
				if (lr != null)
				{
					lr.material.SetColor("_Color", col);
					lr.material.color = col;
				}

				if (markedBones.ContainsKey(lrm.Key))
				{
					List<GameObject> marked = markedBones[lrm.Key];

					if (marked != null)
					{
						foreach (GameObject gom in marked)
						{
							if (gom != null)
							{

								MeshRenderer mr = gom.GetComponent<MeshRenderer>();
								mr.material.color = col;

							}
						}
					}
				}
			}
		}

		public void SetDebugColor(string uniqLr, Color col)
		{
			if (LineRendererMap.ContainsKey(uniqLr))
			{

				LineRenderer lr = LineRendererMap[uniqLr];
				if (lr != null)
				{
					lr.material.SetColor("_Color", col);
					lr.material.color = col;
				}

				if (markedBones.ContainsKey(uniqLr))
				{
					List<GameObject> marked = markedBones[uniqLr];

					if (marked != null)
					{
						foreach (GameObject gom in marked)
						{
							if (gom != null)
							{

								MeshRenderer mr = gom.GetComponent<MeshRenderer>();
								mr.material.color = col;

							}
						}
					}
				}
			}
		}

		public void SetDebugWidth(float width)
		{
			foreach (KeyValuePair<string, LineRenderer> lrm in LineRendererMap)
			{
				LineRenderer lr = lrm.Value;
				if (lr != null)
					if (lr != null)
					{
						lr.startWidth = width;
						lr.endWidth = width;
					}

				/*	if (markedBones.ContainsKey(lrm.Key))
					{
						List<GameObject> marked = markedBones[lrm.Key];

						if (marked != null)
						{
							foreach (GameObject gom in marked)
							{
								if (gom != null)
								{
									gom.transform.localScale = gom.transform.worldToLocalMatrix.MultiplyVector(new Vector3(width * 2, width * 2, width * 2));
								}
							}
						}
					}*/
			}
		}

		public void SetDebugWidth(string uniqLr, float width)
		{
			if (LineRendererMap.ContainsKey(uniqLr))
			{

				LineRenderer lr = LineRendererMap[uniqLr];
				if (lr != null)
					if (lr != null)
					{
						lr.startWidth = width;
						lr.endWidth = width;
					}

				/*	if (markedBones.ContainsKey(uniqLr))
					{
						List<GameObject> marked = markedBones[uniqLr];

						if (marked != null)
						{
							foreach (GameObject gom in marked)
							{
								if (gom != null)
								{
									gom.transform.localScale = gom.transform.worldToLocalMatrix.MultiplyVector(new Vector3(width * 2, width * 2, width * 2));
								}
							}
						}
					}*/
			}
		}

		public void DrawArmatureAutoComplete(string uniqueName, string bone)
		{
			if (bone != null && !bone.Equals(""))
			{
				Transform rootLimb = transforms[bone];
				//string uniqueName = js.val;
				LineRenderer lr;

				if (LineRendererMap.ContainsKey(uniqueName))
					lr = LineRendererMap[uniqueName];
				else
				{
		//			if (rootLimb.gameObject.GetComponent<LineRenderer>() != null)
		//				lr = rootLimb.gameObject.GetComponent<LineRenderer>();
		//			else
						lr = rootLimb.gameObject.AddComponent<LineRenderer>();
					LineRendererMap.Add(uniqueName, lr);
					lr.material = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
					SetDebugColor(uniqueName, ToColor(debugColor.val));
					SetDebugWidth(uniqueName, debugWidth.val);

				}

				List<GameObject> marked;

				if (markedBones.ContainsKey(uniqueName))
				{
					marked = markedBones[uniqueName];
					if (marked != null)
					{
						foreach (GameObject gom in marked)
						{
							DestroyImmediate(gom);
						}
					}
					marked = new List<GameObject>();
					markedBones[uniqueName] = marked;
				}
				else
				{
					marked = new List<GameObject>();
					markedBones.Add(uniqueName, marked);
				}
				List<Vector3> pos = new List<Vector3>();

				Transform[] tt = rootLimb.GetComponentsInChildren<Transform>();

				foreach (Transform bon in tt)
				{
					if (bon.childCount < 2)
					{
						pos.Add(bon.position);
						marked.Add(markTransform(bon));
					}
					else
					{
						int path = 0;
						bool shouldAdd = true;
						for (int i = 0; i < bon.childCount; i++)
						{
							if (path == 0 && bon.GetChild(i).childCount > 0)
								path = bon.GetChild(i).childCount; //item with more one child or more
							else if (path > 0 && bon.GetChild(i).childCount > 0)
								shouldAdd = false;                  //multiple items with one+ child
						}

						if (path > 0 && shouldAdd)
						{
							pos.Add(bon.position);
							marked.Add(markTransform(bon));
						}
						else
							break;
					}
				}

				lr.positionCount = pos.Count;
				lr.SetPositions(pos.ToArray());
			}


		}

		public void DrawArmatureLimb(string uniqueName, string bone1, string bone2, string bone3)
		{
			Transform rootLimb = transforms[bone1];

			LineRenderer lr;

			//string uniqueName = bone1;

			if (LineRendererMap.ContainsKey(uniqueName))
				lr = LineRendererMap[uniqueName];
			else
			{
		///		if (rootLimb.gameObject.GetComponent<LineRenderer>() != null)
			//		lr = rootLimb.gameObject.GetComponent<LineRenderer>();
		//		else
					lr = rootLimb.gameObject.AddComponent<LineRenderer>();
				
				LineRendererMap.Add(uniqueName, lr);
				lr.material = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
				SetDebugColor(uniqueName, ToColor(debugColor.val));
				SetDebugWidth(uniqueName, debugWidth.val);

			}

			List<GameObject> marked;

			if (markedBones.ContainsKey(uniqueName))
			{
				marked = markedBones[uniqueName];
				if (marked != null)
				{
					foreach (GameObject gom in marked)
					{
						DestroyImmediate(gom);
					}
				}
				marked = new List<GameObject>();
				markedBones[uniqueName] = marked;
			}
			else
			{
				marked = new List<GameObject>();
				markedBones.Add(uniqueName, marked);
			}

			List<Vector3> pos = new List<Vector3>();

			pos.Add(rootLimb.position);
			marked.Add(markTransform(rootLimb));

			if (bone2 != null && transforms[bone2] != null)
			{
				Transform bon = transforms[bone2];
				pos.Add(bon.position);
				marked.Add(markTransform(bon));
			}

			if (bone3 != null && transforms[bone3] != null)
			{
				Transform bon = transforms[bone3];
				pos.Add(bon.position);
				marked.Add(markTransform(bon));
			}

			lr.positionCount = pos.Count;
			lr.SetPositions(pos.ToArray());
		}

		public void DrawArmatureChain(string uniqueName, string lrBone, string bone1, List<string> bones)
		{
			Transform rootLimb = transforms[bone1];

			Transform lrBoneT = transforms[lrBone];

			LineRenderer lr;

			if (LineRendererMap.ContainsKey(uniqueName))
				lr = LineRendererMap[uniqueName];
			else
			{
		//		if (rootLimb.gameObject.GetComponent<LineRenderer>() != null)
		//			lr = rootLimb.gameObject.GetComponent<LineRenderer>();
		//		else
					lr = lrBoneT.gameObject.AddComponent<LineRenderer>();
				LineRendererMap.Add(uniqueName, lr);
				lr.material = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
				SetDebugColor(uniqueName, ToColor(debugColor.val));
				SetDebugWidth(uniqueName, debugWidth.val);

			}

			List<GameObject> marked;

			if (markedBones.ContainsKey(uniqueName))
			{
				marked = markedBones[uniqueName];
				if (marked != null)
				{
					foreach (GameObject gom in marked)
					{
						DestroyImmediate(gom);
					}
				}
				marked = new List<GameObject>();
				markedBones[uniqueName] = marked;
			}
			else
			{
				marked = new List<GameObject>();
				markedBones.Add(uniqueName, marked);
			}
			List<Vector3> pos = new List<Vector3>();

			pos.Add(rootLimb.position);
			marked.Add(markTransform(rootLimb));

			foreach (string bone in bones)
			{

				if (bone != null && transforms[bone] != null)
				{
					Transform bon = transforms[bone];
					pos.Add(bon.position);
					marked.Add(markTransform(bon));
				}
				else
					break;

			}

			lr.positionCount = pos.Count;
			lr.SetPositions(pos.ToArray());
		}

		private void DrawArmatureChainSpine(string pelvis, string spine1, string spine2, Dictionary<JSONStorableStringChooser, UIDynamicPopup> spineExtra, string neck, string head, string leye, string reye)
		{
			List<string> bones = new List<string>(new string[] { spine1, spine2 });
			foreach (KeyValuePair<JSONStorableStringChooser, UIDynamicPopup> jsc in spineExtra) { bones.Add(jsc.Key.val); }
			bones.Add(neck);
			bones.Add(head);
			bones.Add(leye);
			bones.Add(reye);
			DrawArmatureChain("FBB_Spine", pelvis, pelvis, bones);
		}

		public void DeleteArmature(string uniqueName)
		{
			if (LineRendererMap.ContainsKey(uniqueName))
			{
				LineRenderer lr = LineRendererMap[uniqueName];
				LineRendererMap.Remove(uniqueName);
				DestroyImmediate(lr);
			}

			if (markedBones.ContainsKey(uniqueName))
			{

				List<GameObject> marked = markedBones[uniqueName];

				markedBones.Remove(uniqueName);

				foreach (GameObject gom in marked)
				{
					DestroyImmediate(gom);
				}

				marked = null;
			}
		}

		protected void recordOriginalTransforms()
		{			
			foreach(KeyValuePair<string, Transform> t in transforms)
			{			
				originalMeshArmature.Add(new object[] { t.Value.localPosition, t.Value.localRotation, t.Value });
			}			
		}

		protected void resetToOriginalTransforms(bool reenableIK)
		{
			Dictionary<string, Atom> needsReset = new Dictionary<string, Atom>();
			Dictionary<int, bool> originalSetting = new Dictionary<int, bool>();		
			bool lookOriginallyOn = false;
			bool headOriginallyOn = false;

			foreach (KeyValuePair<int, VAMIKSolver> solvd in solverDict)
			{
				originalSetting.Add(solvd.Key,solvd.Value.solver.enabled);
				solvd.Value.solver.enabled = false;

				if(solvd.Value.headEffector!=null)
				{
					headOriginallyOn = solvd.Value.headEffector.enabled;
					solvd.Value.headEffector.enabled = false;

					lookOriginallyOn = solvd.Value.solver.GetComponent<LookAtIK>().enabled;
					solvd.Value.solver.GetComponent<LookAtIK>().enabled = false;
					needsReset.Add(((FullBodyBipedIK)solvd.Value.solver).references.head.name, solvd.Value.headEffectorAtom);					
				}

				foreach (KeyValuePair < Transform, Atom> effecter in solvd.Value.effectors )
				{
					needsReset.Add(effecter.Key.name, effecter.Value);
				}
			}
			
			foreach (object[] t in originalMeshArmature)
			{
				Transform originalTrans = (Transform)t[2];

				originalTrans.localPosition = (Vector3)t[0];
				originalTrans.localRotation = (Quaternion)t[1];

				Vector3 pos = originalTrans.position;
				Quaternion rot = originalTrans.rotation;

				if (needsReset.ContainsKey(originalTrans.name))
				{
					needsReset[originalTrans.name].freeControllers[0].transform.position = pos;
					needsReset[originalTrans.name].freeControllers[0].transform.rotation = rot;
				}				
			}
			if(reenableIK)
			{ 

			foreach (KeyValuePair<int, VAMIKSolver> solvd in solverDict)
			{			
				solvd.Value.solver.enabled = originalSetting[solvd.Key];

				if (solvd.Value.headEffector != null)
				{
					solvd.Value.headEffector.enabled = headOriginallyOn;
					solvd.Value.solver.GetComponent<LookAtIK>().enabled = lookOriginallyOn;
				}
			}
			}
		}

		public void resetPose(List<object> bindings)
		{
			if (bindings.Count > 0) //can pass in a bool to reset IK
			{
				bool state = (bool)bindings[0];
				resetToOriginalTransforms(state);
			}
			else
				resetToOriginalTransforms(true);
		}

	/*	public void getFullBodyIKSettings(List<object> bindings)
		{
			foreach(KeyValuePair<int, VAMIKSolver> iks in solverDict)
			{
				if(iks.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{
					bindings.Add(iks.Value.animator); //body animator;
					bindings.Add(iks.Value.effectors); //body animator;
					bindings.Add(iks.Value.headEffectorAtom); //head effector animator;
					bindings.Add(originalMeshArmature);
					break;
				}
			}
		}*/

		public void getOriginalPose(List<object> bindings)
		{
			foreach (KeyValuePair<int, VAMIKSolver> iks in solverDict)
			{
				if (iks.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{
					bindings.Add(originalMeshArmature);
					break;
				}
			}
		}

		public void getFullBodyEffectors(List<object> bindings)
		{
			foreach (KeyValuePair<int, VAMIKSolver> iks in solverDict)
			{
				if (iks.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{	
					bindings.Add(iks.Value.effectors); //body animator;
					bindings.Add(iks.Value.headEffectorAtom); //head effector animator;
					break;
				}
			}
		}

		public void getAnimator(List<object> bindings)
		{
			foreach (KeyValuePair<int, VAMIKSolver> iks in solverDict)
			{
				if (iks.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{
					bindings.Add(iks.Value.animator); //body animator;
					break;
				}
			}
		}

		public override void Init()
		{
			LineRendererMap = new Dictionary<string, LineRenderer>();
			markedBones = new Dictionary<string, List<GameObject>>();
			uiDict = new Dictionary<int, List<UIDynamic>>();
			originalMeshArmature = new List<object[]>();

			issubsceneloading = true;

			if (this.containingAtom.isSubSceneRestore || containingAtom.name.Contains('/'))
				subscene = true;
			else
				subscene = false;

			checkForOtherPlugins();

			SuperController.singleton.onAtomRemovedHandlers += delegate (Atom atom)
			{
				if (atom.Equals(this.containingAtom))
				{
					destroySolvers();
				}
			};			

			ccdSolvers = new JSONStorableString("ccdSolvers", "");
			RegisterString(ccdSolvers);

			fabbrikSolvers = new JSONStorableString("fabbrikSolvers", "");
			RegisterString(fabbrikSolvers);

			limbSolvers = new JSONStorableString("limbSolvers", "");
			RegisterString(limbSolvers);

			fbbSolver = new JSONStorableString("fbbSolver", "");
			RegisterString(fbbSolver);

			solverDict = new Dictionary<int, VAMIKSolver>();
			ikSolverJSON = new JSONStorableStringChooser("ikSolver", null, null, "IK Solver Type");
			ikSolverJSON.choices = solverChoicesFull;

			debugColor = new JSONStorableStringChooser("debugColor", new List<String>(new String[] { "white", "black", "gray", "red", "green", "blue", "cyan", "magenta", "yellow" }), "red", "Debug Color");
			RegisterStringChooser(debugColor);

			UIDynamicPopup dCol = CreatePopup(debugColor, true);
			dCol.popup.onValueChangeHandlers += delegate (string color) { SetDebugColor(ToColor(color)); };

			debugWidth = new JSONStorableFloat("debugWidth", 0.01f, 0.001f, 0.1f);
			RegisterFloat(debugWidth);
			UIDynamicSlider dWidth = CreateSlider(debugWidth, true);
			dWidth.slider.onValueChanged.AddListener(delegate (float f) { SetDebugWidth(f); });

			atomizer = new Atomizer(this);

			//solver type
			UIDynamicPopup solverDrop = CreatePopup(ikSolverJSON);
			solverDrop.popupPanelHeight = 700f;

			UIDynamicButton addIK = CreateButton("Configure IK", false);
			addIK.button.onClick.AddListener(delegate () {				

				setSolver((SolverType)Enum.Parse(typeof(SolverType), ikSolverJSON.val));

				if (ikSolverJSON.val.Equals(SolverType.FullBody.ToString()))
				{
					ikSolverJSON.val = null;
					ikSolverJSON.choices = solverChoicesMinusFBB;
				}
			});

			UIDynamicButton refreshT = CreateButton("Refresh Transforms", false);
			refreshT.button.onClick.AddListener(delegate () {
				refreshTransforms(subscene);
			});			

			refreshTransforms(subscene);

			UIDynamicButton resetButton = CreateButton("Reset Pose", true);
			resetButton.button.onClick.AddListener(delegate () { resetToOriginalTransforms(true); });

			UIDynamicButton resetButton2 = CreateButton("Reset Pose (disable IK)", true);
			resetButton2.button.onClick.AddListener(delegate () { resetToOriginalTransforms(false); });

			}

		private void refreshTransforms(bool forceSS)
		{
			refreshTransforms(getActualContainingGOM(forceSS));
		}

		private void refreshTransforms(GameObject root)
		{
			SkinnedMeshRenderer[] smr = root.GetComponentsInChildren<SkinnedMeshRenderer>();

			transformIds = new List<string>();
			transforms = new Dictionary<string, Transform>();

			foreach (SkinnedMeshRenderer sm in smr)
			{

				Transform[] tt = sm.bones;

				
					foreach (Transform trans in tt)
					{
					if (trans != null)
					{
						if (trans.gameObject.GetComponent<Atom>() != null || trans.gameObject.GetComponent<RectTransform>() != null || trans.gameObject.GetComponent<FreeControllerV3>() != null || trans.gameObject.GetComponent<SubAtom>() != null)
						{
							continue;
						}


						if (transforms.ContainsKey(trans.name))
						{
							if (transforms[trans.name].Equals(trans)) //this is the same bone.. ignore it.
								continue;
							else //a different bone with the same name, add a uniq version of it.
							{
								String uniqName = trans.name;
								int count = 0;
								while (transforms.ContainsKey(uniqName))
								{
									uniqName = trans.name + "_" + count;
									count++;
								}
								transforms.Add(uniqName, trans);
								transformIds.Add(uniqName);
							}
						}
						else
						{
							transforms.Add(trans.name, trans);
							transformIds.Add(trans.name);
						}

					}
				}
			}
		}

		private List<string> getChildTransformNames(string bone)
		{
			List<string> childTransforms = new List<string>();
			if(transforms.ContainsKey(bone))
			{ 
			Transform parentT = transforms[bone];

			Transform[] actualChildren = parentT.GetComponentsInChildren<Transform>();

			foreach (Transform ac in actualChildren)
			{
				if (ac.name.Equals(bone) || markedBones.ContainsKey(bone))
					continue;
				else if (transformIds.Contains(ac.name))
					childTransforms.Add(ac.name);
			}
			}
			return childTransforms;
		}

		private List<Transform> getChildTransforms(string bone)
		{
			List<Transform> childTransforms = new List<Transform>();

			Transform parentT = transforms[bone];

			Transform[] actualChildren = parentT.GetComponentsInChildren<Transform>();

			foreach (Transform ac in actualChildren)
			{
				if (ac.name.Equals(bone))
					continue;
				else if (transformIds.Contains(ac.name))
					childTransforms.Add(ac);
			}

			return childTransforms;
		}

		private List<UIDynamic> setupCCDUI(List<UIDynamic> uicomp, string paramList = "", bool restore = false)
		{
			string boneName = "";
			string effector = "";

			if (restore)
			{
				boneName = paramList.Split('=')[0];
				effector = paramList.Split('=')[1];
			}

			uicomp.Add(CreateLabel("CCD Solver Config", false));

			JSONStorableStringChooser transformJSON = new JSONStorableStringChooser(counter + "_CCD_root", null, null, "Root Bone");
			RegisterStringChooser(transformJSON);

			transformJSON.choices = transformIds;
			transformJSON.val = boneName;

			UIDynamicPopup fp = CreateFilterablePopup(transformJSON);
			fp.popupPanelHeight = 700f;
			uicomp.Add(fp);
			int currentCount = counter;

			fp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureAutoComplete("CCDIK" + currentCount, value);
			};

			bool addedIK = false;
			UIDynamicButton addButton = CreateButton("Add to CUA", false);

			UIDynamicButton removeButton = CreateButton("Remove IK", false);
			removeButton.button.interactable = false;

			addButton.button.onClick.AddListener(delegate () {
				addButton.buttonColor = Color.green;
				DeleteArmature("CCDIK" + currentCount);
				setUpCCD(transformJSON.val, currentCount);
				createIKPostAddUI(currentCount, SolverType.CCD, restore);
				addedIK = true;
				addButton.button.interactable = false;
				removeButton.button.interactable = true;
				fp.popup.topButton.interactable = false;
			});

			addButton.buttonColor = Color.red;
			uicomp.Add(addButton);


			removeButton.button.onClick.AddListener(delegate () {
				cleanupUI(currentCount, addedIK);
				DeleteArmature("CCDIK" + currentCount);
				RemoveButton(removeButton);
			});

			uicomp.Add(removeButton);

			if (restore)
			{
				addButton.buttonColor = Color.green;
				addButton.button.interactable = false;
				removeButton.button.interactable = true;
				setUpCCD(boneName, currentCount, true, effector);
				createIKPostAddUI(currentCount, SolverType.CCD, restore);
			}

			uicomp.Add(CreateSpacer());

			return uicomp;
		}

		private List<UIDynamic> setupFABBRIKUI(List<UIDynamic> uicomp, string paramList = "", bool restore = false)
		{
			string boneName = "";
			string effector = "";

			if (restore)
			{
				boneName = paramList.Split('=')[0];
				effector = paramList.Split('=')[1];
			}

			uicomp.Add(CreateLabel("FABBRIK Solver Config", false));

			JSONStorableStringChooser transformJSON = new JSONStorableStringChooser(counter + "_FABBRIK_root", null, null, "Root Bone");
			RegisterStringChooser(transformJSON);

			transformJSON.choices = transformIds;
			transformJSON.val = boneName;

			UIDynamicPopup fp = CreateFilterablePopup(transformJSON);
			fp.popupPanelHeight = 700f;
			uicomp.Add(fp);
			int currentCount = counter;
			bool addedIK = false;

			fp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureAutoComplete("FABBRIK" + currentCount, value);
			};

			UIDynamicButton resetButton = CreateButton("Add to Bone", false);
			resetButton.button.onClick.AddListener(delegate () {
				DeleteArmature("FABBRIK" + currentCount);
				resetButton.buttonColor = Color.green;
				setUpFABBRIK(transformJSON.val, currentCount);
				addedIK = true;
				resetButton.button.interactable = false;
				fp.popup.topButton.interactable = false;
				createIKPostAddUI(currentCount, SolverType.FABBRIK, restore);
			});

			resetButton.buttonColor = Color.red;
			uicomp.Add(resetButton);

			UIDynamicButton removeButton = CreateButton("Remove IK", false);
			removeButton.button.onClick.AddListener(delegate () {
				cleanupUI(currentCount, addedIK);
				DeleteArmature("FABBRIK" + currentCount);
				RemoveButton(removeButton);


			});

			uicomp.Add(removeButton);


			//setup the effector
			if (restore)
			{
				resetButton.buttonColor = Color.green;
				resetButton.button.interactable = false;
				setUpFABBRIK(boneName, currentCount, true, effector);
				createIKPostAddUI(currentCount, SolverType.FABBRIK, restore);
			}
			uicomp.Add(CreateSpacer());

			return uicomp;
		}

		private List<UIDynamic> setupLimbUI(List<UIDynamic> uicomp, string paramList = "", bool restore = false)
		{

			string boneArrString = "";
			string effector = "";

			string[] boneArr = new string[3];

			if (restore)
			{
				boneArrString = paramList.Split('=')[0];
				effector = paramList.Split('=')[1];
				boneArr = boneArrString.Split(',');
			}

			uicomp.Add(CreateLabel("LimbIK Solver Config", false));

			JSONStorableStringChooser bone1 = new JSONStorableStringChooser(counter + "_Limb_bone1", null, null, "Bone 1");
			bone1.choices = transformIds;
			UIDynamicPopup bone1dp = CreateFilterablePopup(bone1);
			bone1dp.popupPanelHeight = 700f;
			uicomp.Add(bone1dp);
			RegisterStringChooser(bone1);

			JSONStorableStringChooser bone2 = new JSONStorableStringChooser(counter + "_Limb_bone2", null, null, "Bone 2");
			UIDynamicPopup bone2dp = CreateFilterablePopup(bone2);
			bone2dp.popupPanelHeight = 700f;
			uicomp.Add(bone2dp);
			RegisterStringChooser(bone2);


			JSONStorableStringChooser bone3 = new JSONStorableStringChooser(counter + "_Limb_bone3", null, null, "Bone 3");
			UIDynamicPopup bone3dp = CreateFilterablePopup(bone3);
			bone3dp.popupPanelHeight = 700f;
			uicomp.Add(bone3dp);
			RegisterStringChooser(bone3);
			bone3dp.popup.enabled = false;

			bone2dp.popup.topButton.interactable = false;
			bone3dp.popup.topButton.interactable = false;

			if (restore)
			{
				bone1.val = boneArr[0];
				bone2.choices = getChildTransformNames(bone1.val);
				bone2.val = boneArr[1];
				bone3.choices = getChildTransformNames(bone2.val);
				bone3.val = boneArr[2];
			}

			int currentCount = counter;

			bool addedIK = false;
			UIDynamicButton resetButton = CreateButton("Add to CUA", false);
			resetButton.button.onClick.AddListener(delegate () {
				DeleteArmature("LimbIK" + currentCount);
				resetButton.buttonColor = Color.green;
				setUpLimb(bone1.val, bone2.val, bone3.val, currentCount);
				addedIK = true;
				resetButton.button.interactable = false;
				bone1dp.popup.topButton.interactable = false;
				bone2dp.popup.topButton.interactable = false;
				bone3dp.popup.topButton.interactable = false;
				createIKPostAddUI(currentCount, SolverType.Limb, restore);
			});
			uicomp.Add(resetButton);
			resetButton.button.interactable = false;

			bone1dp.popup.onValueChangeHandlers += delegate (string value)
			{
				bone2.val = null;
				bone2.choices = getChildTransformNames(value);
				bone3.val = null;
				bone3.choices = null;
				DrawArmatureLimb("LimbIK" + currentCount, bone1.val, bone2.val, bone3.val);
				bone2dp.popup.topButton.interactable = true;
				bone3dp.popup.topButton.interactable = false;
			};

			bone2dp.popup.onValueChangeHandlers += delegate (string value)
			{
				bone3.choices = getChildTransformNames(value);
				bone3.val = null;
				DrawArmatureLimb("LimbIK" + currentCount, bone1.val, bone2.val, bone3.val);
				bone3dp.popup.topButton.interactable = true;
			};

			bone3dp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureLimb("LimbIK" + currentCount, bone1.val, bone2.val, bone3.val);
				resetButton.button.interactable = true;
			};

			resetButton.buttonColor = Color.red;

			UIDynamicButton removeButton = CreateButton("Remove IK", false);
			removeButton.button.onClick.AddListener(delegate () {
				cleanupUI(currentCount, addedIK);
				DeleteArmature("LimbIK" + currentCount);
				RemoveButton(removeButton);


			});

			uicomp.Add(removeButton);

			if (restore)
			{

				bone1dp.popup.topButton.interactable = false;
				bone2dp.popup.topButton.interactable = false;
				bone3dp.popup.topButton.interactable = false;
				resetButton.buttonColor = Color.green;
				resetButton.button.interactable = false;
				setUpLimb(boneArr[0], boneArr[1], boneArr[2], currentCount, true, effector);
				createIKPostAddUI(currentCount, SolverType.Limb, restore);
			}
			uicomp.Add(CreateSpacer());
			return uicomp;
		}

		private List<UIDynamic> setupFullbodyUI(List<UIDynamic> uicomp, string paramList = "", bool restore = false)
		{
			string pelvisStr = "";
			string spine1Str = "";
			string spine2Str = "";
			string headsStr = "";
			string lthighStr = "";
			string lcalfStr = "";
			string lfootStr = "";
			string rthighStr = "";
			string rcalfStr = "";
			string rfootStr = "";
			string luarmStr = "";
			string lfarmStr = "";
			string lhandStr = "";
			string ruarmStr = "";
			string rfarmStr = "";
			string rhandStr = "";
			string neckStr = "";
			string leyeStr = "";
			string reyeStr = "";

			List<string> spineExtraStr = new List<string>();

			string effectorArrString = "";

			string[] boneArr = null;// new string[20];

			if (restore)
			{
				string boneArrString = paramList.Split('=')[0];
				effectorArrString = paramList.Split('=')[1];
				string[] minusSpine = boneArrString.Split('(');
				boneArr = minusSpine[0].Split(',');
				pelvisStr = boneArr[0];
				spine1Str = boneArr[1];
				spine2Str = boneArr[2];
				headsStr = boneArr[3];
				lthighStr = boneArr[4];
				lcalfStr = boneArr[5];
				lfootStr = boneArr[6];
				rthighStr = boneArr[7];
				rcalfStr = boneArr[8];
				rfootStr = boneArr[9];
				luarmStr = boneArr[10];
				lfarmStr = boneArr[11];
				lhandStr = boneArr[12];
				ruarmStr = boneArr[13];
				rfarmStr = boneArr[14];
				rhandStr = boneArr[15];

				if (boneArr.Length >= 17) neckStr = boneArr[16];
				if (boneArr.Length >= 18) leyeStr = boneArr[17];
				if (boneArr.Length >= 19) reyeStr = boneArr[18];

				if (minusSpine.Length > 1)
				{
					string[] spineStr = minusSpine[1].Split(',');

					if (!spineStr[0].Equals(minusSpine[1]))
					{
						for (int i = 0; i < spineStr.Length; i++)
						{
							spineExtraStr.Add(spineStr[i].Replace(")", ""));
						}

					}
				}

			}

			uicomp.Add(CreateLabel("FullBodyIK Solver Config", false));
			Dictionary<JSONStorableStringChooser, UIDynamicPopup> spineExtra = new Dictionary<JSONStorableStringChooser, UIDynamicPopup>();

			JSONStorableStringChooser pelvis = new JSONStorableStringChooser("pelvis" + counter, null, null, "Pelvis");
			pelvis.choices = transformIds;
			pelvis.val = pelvisStr;
			UIDynamicPopup pelvisdp = CreateFilterablePopup(pelvis);
			pelvisdp.popupPanelHeight = 700f;
			uicomp.Add(pelvisdp);

			JSONStorableStringChooser spine1 = new JSONStorableStringChooser("spine1" + counter, null, null, "spine1");
			UIDynamicPopup spine1dp = CreateFilterablePopup(spine1);
			spine1dp.popupPanelHeight = 700f;
			uicomp.Add(spine1dp);
			spine1dp.popup.topButton.interactable = false;


			JSONStorableStringChooser spine2 = new JSONStorableStringChooser("spine2" + counter, null, null, "spine2");
			UIDynamicPopup spine2dp = CreateFilterablePopup(spine2);
			spine2dp.popupPanelHeight = 700f;
			uicomp.Add(spine2dp);
			spine2dp.popup.topButton.interactable = false;
			int currentUIPos = leftUIElements.Count;

			JSONStorableStringChooser neck = new JSONStorableStringChooser("neck" + counter, null, null, "neck");
			JSONStorableStringChooser head = new JSONStorableStringChooser("head" + counter, null, null, "head");


			GameObject spineControlsPanelGO = new GameObject("spineControlsPanelGO", typeof(RectTransform));
			spineControlsPanelGO.AddComponent<HorizontalLayoutGroup>();
			RectTransform spineControlsPanel = CreateUIElement(spineControlsPanelGO.transform, false).GetComponent<RectTransform>();
			Destroy(spineControlsPanelGO);

			UIDynamicButton addSpine = CreateButton("Add Spine Bone", spineControlsPanel);
			UIDynamicButton delSpine = CreateButton("Delete Spine Bone", spineControlsPanel);
			int spineBoneCount = 3;

			int itemCountHere = leftUIElements.Count;
			uicomp.Add(addSpine);
			uicomp.Add(delSpine);

			if(restore && spineExtraStr.Count > 0)
			{
				int spCount = 3;
				foreach(string spin in spineExtraStr)
				{ 
				JSONStorableStringChooser spineX = new JSONStorableStringChooser("spine" + spCount + counter, null, null, "spine" + spCount);
				UIDynamicPopup spineXdp = CreateFilterablePopup(spineX, false);
				spineXdp.popupPanelHeight = 700f;
				uicomp.Add(spineXdp);
					spineXdp.popup.topButton.interactable = false;
					spineX.val = spin;				
				spCount++;
				}

			}

			UIDynamicPopup neckdp = CreateFilterablePopup(neck);
			neckdp.popupPanelHeight = 700f;
			uicomp.Add(neckdp);
			neckdp.popup.topButton.interactable = false;


			UIDynamicPopup headdp = CreateFilterablePopup(head);
			headdp.popupPanelHeight = 700f;
			uicomp.Add(headdp);
			headdp.popup.topButton.interactable = false;

			JSONStorableStringChooser lthigh = new JSONStorableStringChooser("lthigh" + counter, null, null, "Left Thigh");
			UIDynamicPopup lthighdp = CreateFilterablePopup(lthigh);
			lthighdp.popupPanelHeight = 700f;
			uicomp.Add(lthighdp);
			lthighdp.popup.topButton.interactable = false;
			

			JSONStorableStringChooser lcalf = new JSONStorableStringChooser("lcalf" + counter, null, null, "Left Calf/Shin");
			UIDynamicPopup lcalfdp = CreateFilterablePopup(lcalf);
			lcalfdp.popupPanelHeight = 700f;
			uicomp.Add(lcalfdp);
			lcalfdp.popup.topButton.interactable = false;

			JSONStorableStringChooser lfoot = new JSONStorableStringChooser("lfoot" + counter, null, null, "Left Foot");
			UIDynamicPopup lfootdp = CreateFilterablePopup(lfoot);
			lfootdp.popupPanelHeight = 700f;
			uicomp.Add(lfootdp);
			lfootdp.popup.topButton.interactable = false;

			JSONStorableStringChooser luarm = new JSONStorableStringChooser("luarm" + counter, null, null, "Left Arm");
			UIDynamicPopup luarmdp = CreateFilterablePopup(luarm);
			luarmdp.popupPanelHeight = 700f;
			uicomp.Add(luarmdp);
			luarmdp.popup.topButton.interactable = false;

			JSONStorableStringChooser lfarm = new JSONStorableStringChooser("lfarm" + counter, null, null, "Left Forearm");
			UIDynamicPopup lfarmdp = CreateFilterablePopup(lfarm);
			lfarmdp.popupPanelHeight = 700f;
			uicomp.Add(lfarmdp);
			lfarmdp.popup.topButton.interactable = false;

			JSONStorableStringChooser lhand = new JSONStorableStringChooser("lhand" + counter, null, null, "Left Hand");
			UIDynamicPopup lhanddp = CreateFilterablePopup(lhand);
			lhanddp.popupPanelHeight = 700f;
			uicomp.Add(lhanddp);
			lhanddp.popup.topButton.interactable = false;

			JSONStorableStringChooser rthigh = new JSONStorableStringChooser("rthigh" + counter, null, null, "Right Thigh");
			UIDynamicPopup rthighdp = CreateFilterablePopup(rthigh);
			rthighdp.popupPanelHeight = 700f;
			uicomp.Add(rthighdp);
			rthighdp.popup.topButton.interactable = false;

			JSONStorableStringChooser rcalf = new JSONStorableStringChooser("rcalf" + counter, null, null, "Right Calf/Shin");
			UIDynamicPopup rcalfdp = CreateFilterablePopup(rcalf);
			rcalfdp.popupPanelHeight = 700f;
			uicomp.Add(rcalfdp);
			rcalfdp.popup.topButton.interactable = false;

			JSONStorableStringChooser rfoot = new JSONStorableStringChooser("rfoot" + counter, null, null, "Right Foot");
			UIDynamicPopup rfootdp = CreateFilterablePopup(rfoot);
			rfootdp.popupPanelHeight = 700f;
			uicomp.Add(rfootdp);
			rfootdp.popup.topButton.interactable = false;

			JSONStorableStringChooser ruarm = new JSONStorableStringChooser("ruarm" + counter, null, null, "Right Arm");
			UIDynamicPopup ruarmdp = CreateFilterablePopup(ruarm);
			ruarmdp.popupPanelHeight = 700f;
			uicomp.Add(ruarmdp);
			ruarmdp.popup.topButton.interactable = false;

			JSONStorableStringChooser rfarm = new JSONStorableStringChooser("rfarm" + counter, null, null, "Right Forearm");
			UIDynamicPopup rfarmdp = CreateFilterablePopup(rfarm);
			rfarmdp.popupPanelHeight = 700f;
			uicomp.Add(rfarmdp);
			rfarmdp.popup.topButton.interactable = false;

			JSONStorableStringChooser rhand = new JSONStorableStringChooser("rhand" + counter, null, null, "Right Hand");
			UIDynamicPopup rhanddp = CreateFilterablePopup(rhand);
			rhanddp.popupPanelHeight = 700f;
			uicomp.Add(rhanddp);
			rhanddp.popup.topButton.interactable = false;


			JSONStorableStringChooser leye = new JSONStorableStringChooser("leye" + counter, null, null, "Left Eye(Optional)");
			UIDynamicPopup leyedp = CreateFilterablePopup(leye);
			leyedp.popupPanelHeight = 700f;
			uicomp.Add(leyedp);
			leyedp.popup.topButton.interactable = false;

			JSONStorableStringChooser reye = new JSONStorableStringChooser("reye" + counter, null, null, "Right Eye(Optional)");
			UIDynamicPopup reyedp = CreateFilterablePopup(reye);
			reyedp.popupPanelHeight = 700f;
			uicomp.Add(reyedp);
			reyedp.popup.topButton.interactable = false;

			pelvisdp.popup.onValueChangeHandlers += delegate (string value)
			{
				spine1.val = null;
				spine1.choices = getChildTransformNames(value);
				spine2.val = null;
				spine2.choices = null;
				head.val = null;
				head.choices = null;

				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

				spine1dp.popup.topButton.interactable = true;
				spine2dp.popup.topButton.interactable = false;
				headdp.popup.topButton.interactable = false;
				lthighdp.popup.topButton.interactable = true;
				rthighdp.popup.topButton.interactable = true;

				setupLimbCallbacks("FBB_LLeg", pelvis.val, lthigh, lthighdp, lcalf, lcalfdp, lfoot, lfootdp, rthigh);
				setupLimbCallbacks("FBB_RLeg", pelvis.val, rthigh, rthighdp, rcalf, rcalfdp, rfoot, rfootdp, lthigh);
			};

			spine1dp.popup.onValueChangeHandlers += delegate (string value)
			{
				spine2.val = null;
				spine2.choices = getChildTransformNames(value);
				head.val = null;
				head.choices = null;
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

				spine2dp.popup.topButton.interactable = true;
				headdp.popup.topButton.interactable = false;
			};

			spine2dp.popup.onValueChangeHandlers += delegate (string value)
			{

				neck.val = null;
				neck.choices = getChildTransformNames(value);
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

				neckdp.popup.topButton.interactable = true;
				headdp.popup.topButton.interactable = false;
				luarmdp.popup.topButton.interactable = true;
				ruarmdp.popup.topButton.interactable = true;

				luarm.val = null;
				luarm.choices = getChildTransformNames(spine2.val);
				ruarm.val = null;
				ruarm.choices = getChildTransformNames(spine2.val);

				setupLimbCallbacks("FBB_LArm", spine2.val, luarm, luarmdp, lfarm, lfarmdp, lhand, lhanddp, ruarm);
				setupLimbCallbacks("FBB_RArm", spine2.val, ruarm, ruarmdp, rfarm, rfarmdp, rhand, rhanddp, luarm);

				addSpine.button.interactable = true;
			};

			neckdp.popup.onValueChangeHandlers += delegate (string value)
			{
				head.val = null;
				head.choices = getChildTransformNames(value);
				headdp.popup.topButton.interactable = true;
				leyedp.popup.topButton.interactable = false;
				reyedp.popup.topButton.interactable = false;
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

			};

			headdp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

				leye.val = null;
				leye.choices = getChildTransformNames(value);

				reye.val = null;
				reye.choices = getChildTransformNames(value);

				leyedp.popup.topButton.interactable = true;
				reyedp.popup.topButton.interactable = true;
			};

			leyedp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);
				removeChoice(value, reye);
			};

			reyedp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);
				removeChoice(value, leye);
			};



			addSpine.button.onClick.AddListener(delegate () {

				JSONStorableStringChooser spineX = new JSONStorableStringChooser("spine" + spineBoneCount + counter, null, null, "spine" + spineBoneCount);
				UIDynamicPopup spineXdp = CreateFilterablePopup(spineX, false);
				spineXdp.popupPanelHeight = 700f;
				uicomp.Add(spineXdp);
				spineX.val = null;
				leftUIElements.Add(spineXdp.transform);
				spineXdp.transform.SetSiblingIndex(itemCountHere);

				if (spineExtra.Count > 0)
				{
					spineX.choices = getChildTransformNames(spineExtra.Keys.Last<JSONStorableStringChooser>().val);
				}
				else
					spineX.choices = getChildTransformNames(spine2.val);


				spineXdp.popup.onValueChangeHandlers += delegate (string value)
				{

					DrawArmatureChainSpine(pelvis.val, spine1.val, spine2.val, spineExtra, neck.val, head.val, leye.val, reye.val);

					neck.val = null;
					neck.choices = getChildTransformNames(value);

					addSpine.button.interactable = true;
					neckdp.popup.topButton.interactable = true;
				};

				neckdp.popup.topButton.interactable = false;
				headdp.popup.topButton.interactable = false;

				spineExtra.Add(spineX, spineXdp);
				spineBoneCount++;
				itemCountHere++;

				addSpine.button.interactable = false;
				delSpine.button.interactable = true;
			});
			addSpine.button.interactable = false;

			delSpine.button.onClick.AddListener(delegate ()
			{
				KeyValuePair<JSONStorableStringChooser, UIDynamicPopup> pop = spineExtra.Last<KeyValuePair<JSONStorableStringChooser, UIDynamicPopup>>();
				RemovePopup(pop.Value);
				DeregisterStringChooser(pop.Key);
				spineExtra.Remove(pop.Key);

				if (spineExtra.Count == 0)
					delSpine.button.interactable = false;
				else
					addSpine.button.interactable = true;

				spineBoneCount--;
				itemCountHere--;
			});
			delSpine.button.interactable = false;

			int currentCount = counter;

			bool addedIK = false;
			UIDynamicButton resetButton = CreateButton("Add to CUA", false);
			resetButton.button.onClick.AddListener(delegate () {
				resetButton.buttonColor = Color.green;

				spineExtraStr = new List<string>();
				foreach (KeyValuePair<JSONStorableStringChooser, UIDynamicPopup> sBone in spineExtra)
				{
					spineExtraStr.Add(sBone.Key.val);
				}

				setUpFBB(pelvis.val, lthigh.val, lcalf.val, lfoot.val,
						rthigh.val, rcalf.val, rfoot.val,
						luarm.val, lfarm.val, lhand.val,
						ruarm.val, rfarm.val, rhand.val,
						 spine1.val, spine2.val, spineExtraStr,
						neck.val, head.val, leye.val, reye.val, currentCount);
				addedIK = true;
				resetButton.button.interactable = false;

				DeleteArmature("FBB_Spine");
				DeleteArmature("FBB_LLeg");
				DeleteArmature("FBB_RLeg");
				DeleteArmature("FBB_LArm");
				DeleteArmature("FBB_RArm");

				createIKPostAddUI(currentCount, SolverType.FullBody, restore);
			});
			uicomp.Add(resetButton);

			resetButton.buttonColor = Color.red;

			UIDynamicButton removeButton = CreateButton("Remove IK", false);
			removeButton.button.onClick.AddListener(delegate () {
				cleanupUI(currentCount, addedIK);

				DeleteArmature("FBB_Spine");
				DeleteArmature("FBB_LLeg");
				DeleteArmature("FBB_RLeg");
				DeleteArmature("FBB_LArm");
				DeleteArmature("FBB_RArm");
				ikSolverJSON.choices = solverChoicesFull; //restore the fbb option
				RemoveButton(removeButton);
			});
			uicomp.Add(removeButton);

			if (restore)
			{
				spine1.val = spine1Str;
				spine1.choices = getChildTransformNames(spine1.val);

				spine2.val = spine2Str;
				spine2.choices = getChildTransformNames(spine2.val);

				head.val = headsStr;
				head.choices = getChildTransformNames(head.val);

				lthigh.val = lthighStr;
				lthigh.choices = getChildTransformNames(lthigh.val);

				lcalf.val = lcalfStr;
				lcalf.choices = getChildTransformNames(lcalf.val);

				lfoot.val = lfootStr;
				lfoot.choices = getChildTransformNames(lfoot.val);

				luarm.val = luarmStr;
				luarm.choices = getChildTransformNames(luarm.val);

				lfarm.val = lfarmStr;
				lfarm.choices = getChildTransformNames(lfarm.val);

				lhand.val = lhandStr;
				lhand.choices = getChildTransformNames(lhand.val);

				rthigh.val = rthighStr;
				rthigh.choices = getChildTransformNames(rthigh.val);

				rcalf.val = rcalfStr;
				rcalf.choices = getChildTransformNames(rcalf.val);

				rfoot.val = rfootStr;
				rfoot.choices = getChildTransformNames(rfoot.val);

				ruarm.val = ruarmStr;
				ruarm.choices = getChildTransformNames(ruarm.val);

				rfarm.val = rfarmStr;
				rfarm.choices = getChildTransformNames(rfarm.val);

				rhand.val = rhandStr;
				rhand.choices = getChildTransformNames(rhand.val);

				neck.val = neckStr;
				neck.choices = getChildTransformNames(neck.val);

				leye.val = leyeStr;
				leye.choices = getChildTransformNames(leye.val);

				reye.val = reyeStr;
				reye.choices = getChildTransformNames(reye.val);

				resetButton.buttonColor = Color.green;
				resetButton.button.interactable = false;
				pelvisdp.popup.topButton.interactable = false;
				setUpFBB(pelvisStr, lthighStr, lcalfStr, lfootStr, rthighStr, rcalfStr, rfootStr, luarmStr, lfarmStr, lhandStr, ruarmStr, rfarmStr, rhandStr, spine1Str, spine2Str, spineExtraStr, neckStr, headsStr, leyeStr, reyeStr, currentCount, true, effectorArrString);
				createIKPostAddUI(currentCount, SolverType.FullBody, restore);


			}

			uicomp.Add(CreateSpacer());

			return uicomp;
		}

		private void removeChoice(string choice, JSONStorableStringChooser chooser)
		{
			List<string> orig = chooser.choices;
			if (orig.Contains(choice))
			{
				int index = chooser.choices.IndexOf(choice);
				orig.Remove(choice);
				chooser.choices = null;
				chooser.choices = orig;
			}
		}

		private void setupLimbCallbacks(string uniqueName, string parentBone, JSONStorableStringChooser bone1, UIDynamicPopup bone1dp, JSONStorableStringChooser bone2, UIDynamicPopup bone2dp, JSONStorableStringChooser bone3, UIDynamicPopup bone3dp, JSONStorableStringChooser peerBone = null)
		{
			bone1.val = null;
			bone1.choices = getChildTransformNames(parentBone);

			bone1dp.popup.onValueChangeHandlers += delegate (string value)
			{
				bone2.val = null;
				bone2.choices = getChildTransformNames(value);
				bone3.val = null;
				bone3.choices = null;
				DrawArmatureChain(uniqueName, bone1.val, parentBone, (new string[] { bone1.val, bone2.val, bone3.val }).ToList<string>());
				bone2dp.popup.topButton.interactable = true;
				bone3dp.popup.topButton.interactable = false;

				if (peerBone != null)
					removeChoice(value, peerBone);

			};

			bone2dp.popup.onValueChangeHandlers += delegate (string value)
			{
				bone3.choices = getChildTransformNames(value);
				bone3.val = null;
				DrawArmatureChain(uniqueName, bone1.val, parentBone, (new string[] { bone1.val, bone2.val, bone3.val }).ToList<string>());
				bone3dp.popup.topButton.interactable = true;
			};

			bone3dp.popup.onValueChangeHandlers += delegate (string value)
			{
				DrawArmatureChain(uniqueName, bone1.val, parentBone, (new string[] { bone1.val, bone2.val, bone3.val }).ToList<string>());
			};
		}

		public void restoreSolver(SolverType type_, string paramList)
		{
			List<UIDynamic> uicomp = new List<UIDynamic>();

			if (type_.Equals(SolverType.CCD))
			{
				uicomp = setupCCDUI(uicomp, paramList, true);
			}
			else if (type_.Equals(SolverType.FABBRIK))
			{
				uicomp = setupFABBRIKUI(uicomp, paramList, true);
			}
			else if (type_.Equals(SolverType.Limb))
			{
				uicomp = setupLimbUI(uicomp, paramList, true);
			}
			else if (type_.Equals(SolverType.FullBody))
			{
				uicomp = setupFullbodyUI(uicomp, paramList, true);
			}

			if (uiDict.ContainsKey(counter))
				uiDict[counter].AddRange(uicomp);
			else
				uiDict.Add(counter, uicomp);

			counter++;

		}

		public void setSolver(SolverType type_)
		{
			List<UIDynamic> uicomp = new List<UIDynamic>();

			if (type_.Equals(SolverType.CCD))
			{
				uicomp = setupCCDUI(uicomp);

			}
			else if (type_.Equals(SolverType.FABBRIK))
			{
				uicomp = setupFABBRIKUI(uicomp);

			}
			else if (type_.Equals(SolverType.Limb))
			{
				uicomp = setupLimbUI(uicomp);
			}
			else if (type_.Equals(SolverType.FullBody))
			{
				uicomp = setupFullbodyUI(uicomp);
			}

			uicomp.Add(CreateSpacer());

			if (uiDict.ContainsKey(counter))
				uiDict[counter].AddRange(uicomp);
			else
				uiDict.Add(counter, uicomp);

			counter++;
		}

		void cleanupUI(int itemNum, bool addedIK)
		{
			foreach (UIDynamic dd in uiDict[itemNum])
			{
				if (dd.GetType().Equals(typeof(UIDynamicPopup)))
				{
					RemovePopup((UIDynamicPopup)dd);
					DestroyImmediate(dd);
				}
				else if (dd.GetType().Equals(typeof(UIDynamicButton)))
				{
					RemoveButton((UIDynamicButton)dd);
					DestroyImmediate(dd);
				}
				else if (dd.GetType().Equals(typeof(UIDynamicTextField)))
				{
					RemoveTextField((UIDynamicTextField)dd);
					DestroyImmediate(dd);
				}
				else if (dd.GetType().Equals(typeof(UIDynamicToggle)))
				{
					RemoveToggle((UIDynamicToggle)dd);
					DestroyImmediate(dd);
				}
				else if (dd.GetType().Equals(typeof(UIDynamicSlider)))
				{
					RemoveSlider((UIDynamicSlider)dd);
					DestroyImmediate(dd);
				}
				else if (dd.GetType().Equals(typeof(UIDynamic)))
				{
					RemoveSpacer((UIDynamic)dd);
					DestroyImmediate(dd);
				}
			}

			if (solverDict.ContainsKey(itemNum))
			{

				foreach (KeyValuePair<Transform, Atom> atrco in solverDict[itemNum].effectors)
				{
					SuperController.singleton.RemoveAtom(atrco.Value);
				}

				if (solverDict[itemNum].headEffector != null)
					Destroy(solverDict[itemNum].headEffector);

				if (solverDict[itemNum].headEffectorAtom != null)
					SuperController.singleton.RemoveAtom(solverDict[itemNum].headEffectorAtom);

				Destroy(solverDict[itemNum].solver);

			}

		}

		public void Update()
		{
			atomizer.Update();
			RefreshLineRenderer();

			if (checkSolvers)
				StartCoroutine(enableIKSolvers());
		}

		protected void RefreshLineRenderer()
		{

			foreach (KeyValuePair<string, LineRenderer> lrm in LineRendererMap)
			{
				LineRenderer lr = lrm.Value;


				if (lr != null)
				{

					if (markedBones.ContainsKey(lrm.Key))
					{

						List<GameObject> marked = markedBones[lrm.Key];

						if (marked != null)
						{

							List<Vector3> pos = new List<Vector3>();
							foreach (GameObject gom in marked)
							{
								pos.Add(gom.transform.position);
							}
							lr.positionCount = pos.Count;
							lr.SetPositions(pos.ToArray());
						}
					}

				}

			}
		}

		private void setUpCCD(string bone, int counted, bool restore = false, string effector = null)
		{
			if (solverDict.ContainsKey(counted))
			{
				Destroy(solverDict[counted].solver); //XXX				
				solverDict.Remove(counted);
			}
			if (!transforms.ContainsKey(bone))
				refreshTransforms(subscene);

			Transform rootLimb = transforms[bone];

			CCDIK ccd = rootLimb.gameObject.AddComponent<CCDIK>();
			ccd.enabled = false;
			ccd.solver.useRotationLimits = true;
			Transform[] tt = rootLimb.GetComponentsInChildren<Transform>();

			foreach (Transform bon in tt)
			{
				if (bon.childCount < 2)
					ccd.solver.AddBone(bon);
				else
				{
					int path = 0;
					bool shouldAdd = true;
					for (int i = 0; i < bon.childCount; i++)
					{
						if (path == 0 && bon.GetChild(i).childCount > 0)
							path = bon.GetChild(i).childCount; //item with more one child or more
						else if (path > 0 && bon.GetChild(i).childCount > 0)
							shouldAdd = false;                  //multiple items with one+ child
					}

					if (path > 0 && shouldAdd)
					{
						ccd.solver.AddBone(bon);
					}
					else
						break;
				}
			}

			ccd.fixTransforms = true;
			ccd.solver.SetIKPosition(tt.Last<Transform>().position);
			ccd.solver.FadeOutBoneWeights();
			ccd.solver.SetIKPositionWeight(1f);
			
			VAMIKSolver vamEnt = new VAMIKSolver(ccd);
			solverDict.Add(counted, vamEnt);

			String uniq = DateTime.Now.ToString("HHmmss");

			if (restore && effector != null)
			{
				Atom effectorAtom = SuperController.singleton.GetAtomByUid(effector);

				if (effectorAtom == null && subscene)
				{
					effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effector);
				}

				ccd.solver.target = effectorAtom.freeControllers[0].transform;
				effectorAtom.SetParentAtom(getActualContainingAtom());
				vamEnt.addEffector(effectorAtom, tt.Last<Transform>());

			}
			else
				atomizer.CreateAtom("Empty", counted + "_" + uniq + "_CCD_Effector", "", atomCreatedCCD);

			if (!restore)
			{
				ccdSolvers.val = ccdSolvers.val + "|" + bone + "=" + counted + "_" + uniq + "_CCD_Effector";

				checkSolvers = true;
			}
		}

		private void setUpFABBRIK(string bone, int counted, bool restore = false, string effector = null)
		{


			if (solverDict.ContainsKey(counted))
			{
				Destroy(solverDict[counted].solver);
				solverDict.Remove(counted);
			}

			if (!transforms.ContainsKey(bone))
				refreshTransforms(subscene);

			Transform rootLimb = transforms[bone];

			FABRIK ccd = rootLimb.gameObject.AddComponent<FABRIK>();
			ccd.enabled = false;
			ccd.solver.useRotationLimits = false;

			Transform[] tt = rootLimb.GetComponentsInChildren<Transform>();
			foreach (Transform bon in tt)
			{
				if (bon.childCount < 2)
					ccd.solver.AddBone(bon);
				else
				{
					int path = 0;
					bool shouldAdd = true;
					for (int i = 0; i < bon.childCount; i++)
					{
						if (path == 0 && bon.GetChild(i).childCount > 0)
							path = bon.GetChild(i).childCount; //item with more one child or more
						else if (path > 0 && bon.GetChild(i).childCount > 0)
							shouldAdd = false;                  //multiple items with one+ child
					}

					if (path > 0 && shouldAdd)
					{
						ccd.solver.AddBone(bon);
					}
					else
						break;
				}
			}

			String uniq = DateTime.Now.ToString("HHmmss");

			ccd.fixTransforms = true;
			ccd.solver.SetIKPosition(tt.Last<Transform>().position);
			//ccd.solver.FadeOutBoneWeights();
			ccd.solver.SetIKPositionWeight(1f);
			
			VAMIKSolver vamEnt = new VAMIKSolver(ccd);
			solverDict.Add(counted, vamEnt);

			if (restore && effector != null)
			{
				Atom effectorAtom = SuperController.singleton.GetAtomByUid(effector);
				if (effectorAtom == null && subscene)
				{
					effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effector);
				}


				ccd.solver.target = effectorAtom.freeControllers[0].transform;
				effectorAtom.SetParentAtom(getActualContainingAtom());
				vamEnt.addEffector(effectorAtom,tt.Last<Transform>());
			}
			else
				atomizer.CreateAtom("Empty", counted + "_" + uniq + "_FABBRIK_Effector", "", atomCreatedFABBRIK);


			if (!restore)
			{
				fabbrikSolvers.val = fabbrikSolvers.val + "|" + bone + "=" + counted + "_" + uniq + "_FABBRIK_Effector";

				checkSolvers = true;
			}
		}

		private void setUpLimb(string bone1_, string bone2_, string bone3_, int counted, bool restore = false, string effector = null)
		{


			if (solverDict.ContainsKey(counted))
			{
				Destroy(solverDict[counted].solver);
				solverDict.Remove(counted);
			}
			if (!transforms.ContainsKey(bone1_) || !transforms.ContainsKey(bone2_) || !transforms.ContainsKey(bone3_))
				refreshTransforms(subscene);

			Transform bone1 = transforms[bone1_];
			Transform bone2 = transforms[bone2_];
			Transform bone3 = transforms[bone3_];

			LimbIK limbkk = bone1.gameObject.AddComponent<LimbIK>();
			limbkk.enabled = false;

			limbkk.solver.SetChain(bone1, bone2, bone3, bone1);

			limbkk.fixTransforms = true;
			limbkk.solver.SetIKRotationWeight(1f);
			limbkk.solver.SetIKPositionWeight(1f);
			
			VAMIKSolver vamEnt = new VAMIKSolver(limbkk);
			solverDict.Add(counted, vamEnt);
			String uniq = DateTime.Now.ToString("HHmmss");

			if (restore && effector != null)
			{
				Atom effectorAtom = SuperController.singleton.GetAtomByUid(effector);
				if (effectorAtom == null && subscene)
				{
					effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effector);
				}
				
				Vector3 position = bone3.position;
				Quaternion rotatio = bone3.rotation;

				limbkk.solver.target = effectorAtom.freeControllers[0].transform;
			
				effectorAtom.freeControllers[0].transform.position = position;
				effectorAtom.freeControllers[0].transform.rotation = rotatio;

				effectorAtom.SetParentAtom(getActualContainingAtom());
				vamEnt.addEffector(effectorAtom, bone3);
			}
			else
				atomizer.CreateAtom("Empty", counted + "_" + uniq + "_LimbIK_Effector", "", atomCreatedLimb);

			if (!restore)
			{
				limbSolvers.val = limbSolvers.val + "|" + bone1_ + "," + bone2_ + "," + bone3_ + "=" + counted + "_" + uniq + "_LimbIK_Effector";
				checkSolvers = true;
			}

			
			
		}

		private void setUpFBB(string pelvis, string lthigh, string lcalf, string lfoot,
								string rthigh, string rcalf, string rfoot,
								string luarm, string lfarm, string lhand,
								string ruarm, string rfarm, string rhand,
								string spine1, string spine2, List<string> extraSpine,
								string neck, string head, string leye, string reye, int counted,
								bool restore = false, string effector = null)
		{
			

			if (solverDict.ContainsKey(counted))
			{
				Destroy(solverDict[counted].solver);
				solverDict.Remove(counted);
			}
			if (!transforms.ContainsKey(pelvis) || !transforms.ContainsKey(rthigh) || !transforms.ContainsKey(head))
				refreshTransforms(subscene);

			recordOriginalTransforms();

			Transform pelvis_ = transforms[pelvis];
			Transform lthigh_ = transforms[lthigh];
			Transform lcalf_ = transforms[lcalf];
			Transform lfoot_ = transforms[lfoot];

			Transform rthigh_ = transforms[rthigh];
			Transform rcalf_ = transforms[rcalf];
			Transform rfoot_ = transforms[rfoot];

			Transform luarm_ = transforms[luarm];
			Transform lfarm_ = transforms[lfarm];
			Transform lhand_ = transforms[lhand];

			Transform ruarm_ = transforms[ruarm];
			Transform rfarm_ = transforms[rfarm];
			Transform rhand_ = transforms[rhand];

			Transform spine1_ = transforms[spine1];
			Transform spine2_ = transforms[spine2];

			Transform neck_ = null;
			Transform head_ = transforms[head];
			Transform leye_ = null;
			Transform reye_ = null;

			if (neck != null && neck.Length > 0)
				neck_ = transforms[neck];

			List<Transform> eyes = new List<Transform>();

			if (leye != null && leye.Length > 0)
			{
				leye_ = transforms[leye];
				eyes.Add(leye_);
			}


			if (reye != null && reye.Length > 0)
			{
				reye_ = transforms[reye];
				eyes.Add(reye_);
			}

			List<Transform> spineCol = new List<Transform>();
			spineCol.Add(spine1_);
			spineCol.Add(spine2_);

			if (extraSpine != null && extraSpine.Count > 0)
			{
				foreach (string spineBone in extraSpine)
					spineCol.Add(transforms[spineBone]);
			}

			List<Transform> chestBones = new List<Transform>();
			chestBones.Add(spineCol.Last<Transform>());

			if (neck_ != null)
			{
				spineCol.Add(neck_);
				chestBones.Add(neck_);
			}

			chestBones.Add(head_);

			FullBodyBipedIK fbb = pelvis_.parent.gameObject.AddComponent<FullBodyBipedIK>();

			fbb.enabled = false;
			fbb.solver.rootNode = spine1_;
			fbb.references.head = head_;
			fbb.references.leftCalf = lcalf_;
			fbb.references.leftThigh = lthigh_;
			fbb.references.leftFoot = lfoot_;

			fbb.references.rightCalf = rcalf_;
			fbb.references.rightThigh = rthigh_;
			fbb.references.rightFoot = rfoot_;

			fbb.references.leftUpperArm = luarm_;
			fbb.references.leftForearm = lfarm_;
			fbb.references.leftHand = lhand_;

			fbb.references.rightUpperArm = ruarm_;
			fbb.references.rightForearm = rfarm_;
			fbb.references.rightHand = rhand_;

			fbb.references.pelvis = pelvis_;
			fbb.references.spine = spineCol.ToArray();
			fbb.references.eyes = eyes.ToArray();
			fbb.references.root = pelvis_.parent.transform;

			fbb.solver.SetToReferences(fbb.references);

			fbb.fixTransforms = true;

			fbb.solver.IKPositionWeight = 1f;

			fbb.solver.spineMapping.twistWeight = 1f;
			fbb.solver.headMapping.maintainRotationWeight = 1f;
			VAMIKSolver vamkent = new VAMIKSolver(fbb);
			solverDict.Add(counted, vamkent);
			string effectorList = "";

            //Head Effector
            #region head effector
            GameObject headEffector = new GameObject("headeff");
			headEffector.transform.position = head_.position;
			headEffector.transform.rotation = head_.rotation;

			FBBIKHeadEffector headEff = headEffector.AddComponent<FBBIKHeadEffector>();
			headEff.enabled = false;
			headEff.ik = fbb;

			List<FBBIKHeadEffector.BendBone> bendBones = new List<FBBIKHeadEffector.BendBone>();

			int count = 0;
			float percent = 1f / spineCol.Count - 1;

			foreach (Transform spineBone in spineCol)
			{
				bendBones.Add(new FBBIKHeadEffector.BendBone(spineBone, percent * count));
				count++;
			}

			headEff.bendBones = bendBones.ToArray();
			headEff.chestBones = new Transform[0];
			headEff.CCDBones = new Transform[0];
			headEff.chestDirectionWeight = 1f;
			headEff.roll = 1f;
			headEff.damper = 500f;
			headEff.CCDWeight = 0f;
			headEff.headClampWeight = 0.8f;
			headEff.bodyClampWeight = 0.1f;
			headEff.stretchBones = chestBones.ToArray();
			headEff.stretchDamper = 1f;
			headEff.handsPullBody = true;
			headEff.positionWeight = .8f;
			headEff.rotationWeight = 0f;
			headEff.bendWeight = 1f;

			vamkent.headEffector = headEff;
            #endregion

            Dictionary<string, string> effectorMapping = new Dictionary<string, string>();

			if (effector != null)
			{
				string[] effies = effector.Split(',');

				foreach (string efti in effies)
				{
					effectorMapping.Add(efti.Split('&')[1], efti);
				}
			}

			String uniq = DateTime.Now.ToString("HHmmss");

			foreach (IKEffector eff in fbb.solver.effectors)
			{
				eff.positionWeight = 1f;
				eff.rotationWeight = 1f;
				eff.maintainRelativePositionWeight = 1f;
				if (restore && effector != null && effectorMapping.ContainsKey(eff.bone.name))
				{
					Atom effectorAtom = SuperController.singleton.GetAtomByUid(effectorMapping[eff.bone.name]);
					if (effectorAtom == null && subscene)
					{
						effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effectorMapping[eff.bone.name]);
					}

					eff.target = effectorAtom.freeControllers[0].transform;
					effectorAtom.SetParentAtom(getActualContainingAtom(subscene));
					vamkent.addEffector(effectorAtom, eff.bone);

					effectorList = effectorList.Length == 0 ? effectorMapping[eff.bone.name] : effectorList + "," + effectorMapping[eff.bone.name];

				}
				else
				{
					string effectorName = counted + "&" + eff.bone.name + "&" + uniq + "FBBIK_Effector";
					atomizer.CreateAtom("Empty", effectorName, "", atomCreatedFullBody);
					effectorList = effectorList.Length == 0 ? effectorName : effectorList + "," + effectorName;
				}

			}
			
			foreach(FBIKChain chain in fbb.solver.chain)
			{
				chain.bendConstraint.weight = 1f;
				
				if (restore && chain != null && chain.bendConstraint!=null && chain.bendConstraint.bone2!=null && effectorMapping.ContainsKey(chain.bendConstraint.bone2.name))
				{
					Atom effectorAtom = SuperController.singleton.GetAtomByUid(effectorMapping[chain.bendConstraint.bone2.name]);
					if (effectorAtom == null && subscene)
					{
						effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effectorMapping[chain.bendConstraint.bone2.name]);
					}

					chain.bendConstraint.bendGoal = effectorAtom.freeControllers[0].transform;
					effectorAtom.SetParentAtom(getActualContainingAtom(subscene));
					vamkent.addEffector(effectorAtom, chain.bendConstraint.bone2);
					effectorList = effectorList.Length == 0 ? effectorMapping[chain.bendConstraint.bone2.name] : effectorList + "," + effectorMapping[chain.bendConstraint.bone2.name];
				}
				else if (chain != null && chain.bendConstraint != null && chain.bendConstraint.bone2 != null)
				{
					string effectorName = counted + "&" + chain.bendConstraint.bone2.name + "&" + uniq + "FBBIK_BendGoal";
					atomizer.CreateAtom("Empty", effectorName, "", atomCreatedFullBody);
					effectorList = effectorList.Length == 0 ? effectorName : effectorList + "," + effectorName;
				}
			}	

			foreach (IKMappingLimb limb in fbb.solver.limbMappings)
			{
				limb.maintainRotationWeight = 1f;
				limb.weight = 1f;
			}

			if (headEff != null)
			{
				if (restore && effectorMapping.ContainsKey(head))
				{
					Atom effectorAtom = SuperController.singleton.GetAtomByUid(effectorMapping[head]);
					if (effectorAtom == null && subscene)
					{
						effectorAtom = SuperController.singleton.GetAtomByUid(containingAtom.subScenePath + effectorMapping[head]);
					}

					headEffector.transform.position = effectorAtom.freeControllers[0].transform.position;
					headEffector.transform.rotation = effectorAtom.freeControllers[0].transform.rotation;

					headEffector.transform.parent = effectorAtom.freeControllers[0].transform;
					effectorAtom.SetParentAtom(getActualContainingAtom(subscene));
					vamkent.headEffectorAtom = effectorAtom;
					effectorList = effectorList.Length == 0 ? effectorMapping[head] : effectorList + "," + effectorMapping[head];
				}
				else
				{
					string effectorName = counted + "&" + head + "&" + uniq + "FBBIK_Effector";
					atomizer.CreateAtom("Empty", effectorName, "", atomCreatedHeadEffector);
					effectorList = effectorList.Length == 0 ? effectorName : effectorList + "," + effectorName;
				}
			}

			LookAtIK look = pelvis_.parent.gameObject.AddComponent<LookAtIK>();
			look.solver.eyes = new IKSolverLookAt.LookAtBone[0];
			look.solver.head = new IKSolverLookAt.LookAtBone(head_);

			List<IKSolverLookAt.LookAtBone> spineLook = new List<IKSolverLookAt.LookAtBone>();
			foreach(Transform spBone in spineCol) spineLook.Add(new IKSolverLookAt.LookAtBone(spBone));
			look.solver.spine = spineLook.ToArray();
			look.solver.target = headEffector.transform;// SuperController.singleton.centerCameraTarget.transform;

			if (leye_!=null && reye_!=null)
			{
				look.solver.eyes = new IKSolverLookAt.LookAtBone[ ] { new IKSolverLookAt.LookAtBone(leye_), new IKSolverLookAt.LookAtBone(reye_) };

                #region animator and lookcontroller.
                	Dictionary<string, HumanBodyBones> mapping = new Dictionary<string, HumanBodyBones>();
					List<Transform> bonesInUse = new List<Transform>();

					mapping.Add(pelvis_.name, HumanBodyBones.Hips); bonesInUse.Add(pelvis_);

					mapping.Add(lthigh_.name, HumanBodyBones.LeftUpperLeg ); bonesInUse.Add(lthigh_);
					mapping.Add(lcalf_.name, HumanBodyBones.LeftLowerLeg ); bonesInUse.Add(lcalf_);
					mapping.Add(lfoot_.name, HumanBodyBones.LeftFoot ); bonesInUse.Add(lfoot_);

					mapping.Add(rthigh_.name, HumanBodyBones.RightUpperLeg ); bonesInUse.Add(rthigh_);
					mapping.Add(rcalf_.name, HumanBodyBones.RightLowerLeg ); bonesInUse.Add(rcalf_);
					mapping.Add(rfoot_.name, HumanBodyBones.RightFoot ); bonesInUse.Add(rfoot_);

					mapping.Add(luarm_.parent.name, HumanBodyBones.LeftShoulder); bonesInUse.Add(luarm_.parent);
					mapping.Add(luarm_.name, HumanBodyBones.LeftUpperArm ); bonesInUse.Add(luarm_);
					mapping.Add(lfarm_.name, HumanBodyBones.LeftLowerArm ); bonesInUse.Add(lfarm_);
					mapping.Add(lhand_.name, HumanBodyBones.LeftHand ); bonesInUse.Add(lhand_);

					mapping.Add(ruarm_.parent.name, HumanBodyBones.RightShoulder); bonesInUse.Add(ruarm_.parent);
					mapping.Add(ruarm_.name, HumanBodyBones.RightUpperArm); bonesInUse.Add(ruarm_);
					mapping.Add(rfarm_.name, HumanBodyBones.RightLowerArm); bonesInUse.Add(rfarm_);
					mapping.Add(rhand_.name, HumanBodyBones.RightHand); bonesInUse.Add(rhand_);

					mapping.Add(spine1_.name, HumanBodyBones.Spine ); bonesInUse.Add(spine1_);
					mapping.Add(spine2_.name, HumanBodyBones.Chest ); bonesInUse.Add(spine2_);

					mapping.Add(neck_.name, HumanBodyBones.Neck); bonesInUse.Add(neck_);
					mapping.Add(head_.name, HumanBodyBones.Head); bonesInUse.Add(head_);
					mapping.Add(leye_.name, HumanBodyBones.LeftEye); bonesInUse.Add(leye_);
					mapping.Add(reye_.name, HumanBodyBones.RightEye); bonesInUse.Add(reye_);
				
					bonesInUse = getChildTransforms(pelvis_.parent.name);
					bonesInUse.Add(pelvis_.parent);
					Transform parentHolder = pelvis_.parent.parent;

					pelvis_.parent.parent = null;

					Animator ar = pelvis_.parent.gameObject.AddComponent<Animator>();
					ar.enabled = false;

					Avatar av = createAvatar(mapping, bonesInUse, pelvis_.parent.gameObject);
					pelvis_.parent.parent = parentHolder;

					ar.avatar = av;

					av.name = pelvis;

					ar.applyRootMotion = true;

					ar.enabled = true;
					vamkent.animator = ar;

			/*		EyeAndHeadAnimator eaha = pelvis_.parent.gameObject.AddComponent<EyeAndHeadAnimator>();

					eaha.enabled = false;
					eaha.controlData = new ControlData();

					eaha.controlData.leftEye = leye_;
					eaha.controlData.rightEye = reye_;
					eaha.controlData.eyelidControl = ControlData.EyelidControl.None;
					eaha.controlData.eyelidBoneMode = ControlData.EyelidBoneMode.RotationAndPosition;
					eaha.controlData.CheckConsistency(ar, eaha);
					eaha.controlData.Initialize();
					eaha.enabled = true;
					LookTargetController ltc = pelvis_.parent.gameObject.AddComponent<LookTargetController>();
					ltc.lookAtPlayerRatio = 0.8f;
					ltc.OnStopLookingAtPlayer = new UnityEngine.Events.UnityEvent();
					ltc.OnStartLookingAtPlayer = new UnityEngine.Events.UnityEvent();

					ltc.playerEyeCenter = SuperController.singleton.centerCameraTarget.transform;


					ltc.Initialize();
					*/
				#endregion
			}

		//	this.containingAtom.gameObject.AddComponent<ElkVR.BVHPlayer>();


			if (!restore) {



				string solverString = pelvis + "," + spine1 + "," + spine2 + "," + head
									 + "," + lthigh + "," + lcalf + "," + lfoot
									 + "," + rthigh + "," + rcalf + "," + rfoot
									 + "," + luarm + "," + lfarm + "," + lhand
									 + "," + ruarm + "," + rfarm + "," + rhand
									 + "," + neck;

				if (leye != null)
					solverString = solverString + "," + leye;

				if (reye != null)
					solverString = solverString + "," + reye;

				if (extraSpine.Count > 0)
				{
					string spineExtraString = "";

					foreach (string spin in extraSpine)
						spineExtraString = spineExtraString.Length == 0 ? "(" + spin : spineExtraString + "," + spin;

					spineExtraString = spineExtraString + ")";

					solverString = solverString + "," + spineExtraString;
				}

				fbbSolver.val = solverString + "=" + effectorList;

				checkSolvers = true;
			}

		}

		public IEnumerator enableIKSolvers()
		{


			foreach (KeyValuePair<int, VAMIKSolver> vamkent in solverDict)
			{
				if (vamkent.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{
					if (vamkent.Value.newlyAddedSolver)
				{

						
							vamkent.Value.solver.enabled = true;

							yield return new WaitForSeconds(2);

							if (vamkent.Value.headEffector != null) vamkent.Value.headEffector.enabled = false; //never start with the head effector on
							vamkent.Value.newlyAddedSolver = false;
						
					}
				}
			}


			foreach (KeyValuePair<int, VAMIKSolver> vamkent in solverDict)
			{
				if (!vamkent.Value.solver.GetType().Equals(typeof(FullBodyBipedIK)))
				{
					if (vamkent.Value.newlyAddedSolver)
				{

						vamkent.Value.solver.enabled = true;
						vamkent.Value.newlyAddedSolver = false;
					}
				}
			}

			checkSolvers = false;
		}

		public void  destroySolvers()
		{

			foreach (KeyValuePair<int, VAMIKSolver> vamkent in solverDict)
			{
				if(vamkent.Value.headEffector!=null)
					Destroy(vamkent.Value.headEffector);

				Destroy(vamkent.Value.solver);
			}

		}

		public void atomCreatedLimb(Atom atcre)
		{
			MeshRenderer mResh = atcre.GetComponentInChildren<MeshRenderer>();
			if (mResh != null)
				Destroy(mResh);

			int countE = int.Parse(atcre.name.Split('_')[0]);
			LimbIK limbk = ((LimbIK)solverDict[countE].solver);
			
			

			Vector3 position = limbk.solver.bone3.transform.position;
			Quaternion rotatio = limbk.solver.bone3.transform.rotation;

			limbk.solver.target = atcre.freeControllers[0].transform;
			
			atcre.freeControllers[0].transform.position = position;
			atcre.freeControllers[0].transform.rotation = rotatio;


			atcre.parentAtom = getActualContainingAtom();

			solverDict[countE].addEffector(atcre, limbk.solver.bone3.transform);
		}

		public void atomCreatedCCD(Atom atcre)
		{
			MeshRenderer mResh = atcre.GetComponentInChildren<MeshRenderer>();
			if (mResh != null)
				Destroy(mResh);

			int countE = int.Parse(atcre.name.Split('_')[0]);
			CCDIK ccd = ((CCDIK)solverDict[countE].solver);
			
			atcre.freeControllers[0].transform.position = ccd.solver.GetIKPosition();

			ccd.solver.target = atcre.freeControllers[0].transform;

			atcre.parentAtom = getActualContainingAtom();

			solverDict[countE].addEffector(atcre, ccd.solver.bones.Last<IKSolver.Bone>().transform);
		}

		public void atomCreatedFABBRIK(Atom atcre)
		{
			MeshRenderer mResh = atcre.GetComponentInChildren<MeshRenderer>();
			if (mResh != null)
				Destroy(mResh);

			int countE = int.Parse(atcre.name.Split('_')[0]);
			FABRIK ccd = ((FABRIK)solverDict[countE].solver);

			atcre.freeControllers[0].transform.position = ccd.solver.GetIKPosition();

			ccd.solver.target = atcre.freeControllers[0].transform;

			atcre.parentAtom = getActualContainingAtom();

			solverDict[countE].addEffector(atcre, ccd.solver.bones.Last<IKSolver.Bone>().transform);
		}

		public void atomCreatedFullBody(Atom atcre)
		{
			String[] vals = atcre.name.Split('&');

			int countE = int.Parse(vals[0]);
			String boneName = vals[1];

			MeshRenderer mResh = atcre.GetComponentInChildren<MeshRenderer>();
			if (mResh != null)
				Destroy(mResh);


			if (atcre.name.Contains("Effector"))
			{ 
			foreach (IKEffector eff in ((FullBodyBipedIK)solverDict[countE].solver).solver.effectors)
			{

				if ((eff.bone.name).Equals(boneName))
				{
					Vector3 position = transforms[boneName].position;
					Quaternion rotatio = transforms[boneName].rotation;

					eff.target = atcre.freeControllers[0].transform;
					atcre.freeControllers[0].transform.position = position;
					atcre.freeControllers[0].transform.rotation = rotatio;
					atcre.parentAtom = getActualContainingAtom();
					solverDict[countE].addEffector(atcre, transforms[boneName]);
					
					break;
				}

			}
			}
			else if(atcre.name.Contains("BendGoal"))
			{
				foreach (FBIKChain chain in ((FullBodyBipedIK)solverDict[countE].solver).solver.chain)
				{

					if (chain.bendConstraint.bone2!=null && (chain.bendConstraint.bone2.name).Equals(boneName))
					{
						Vector3 position = transforms[boneName].position;
						Quaternion rotatio = transforms[boneName].rotation;

						chain.bendConstraint.bendGoal = atcre.freeControllers[0].transform;
						atcre.freeControllers[0].transform.position = position;
						atcre.freeControllers[0].transform.rotation = rotatio;
						atcre.parentAtom = getActualContainingAtom();
						solverDict[countE].addEffector(atcre, transforms[boneName]);

						break;
					}

				}
			}

		}

		public void atomCreatedHeadEffector(Atom atcre)
		{
			String[] vals = atcre.name.Split('&');

			int countE = int.Parse(vals[0]);
			String boneName = vals[1];

			Vector3 position = transforms[boneName].position;
			Quaternion rotatio = transforms[boneName].rotation;
			atcre.freeControllers[0].transform.position = position;
			atcre.freeControllers[0].transform.rotation = rotatio;
			atcre.parentAtom = getActualContainingAtom();
			solverDict[countE].headEffector.transform.parent = atcre.freeControllers[0].transform;
		}

		public UIDynamicTextField CreateLabel(string label, bool rhs, int height = 40)
		{
			JSONStorableString jsonLabel = new JSONStorableString(label, label);
			UIDynamicTextField labelField = CreateTextField(jsonLabel, rhs);
			SetTextFieldHeight(labelField, height);

			return labelField;
		}

		public static void SetTextFieldHeight(UIDynamicTextField textField, int height)
		{
			LayoutElement component = textField.GetComponent<LayoutElement>();
			if (component != null)
			{
				component.minHeight = height;
				component.preferredHeight = height;
			}
			textField.height = height;
		}

		protected UIDynamicSlider createIKFloatSlider(string name, string displayName, float initialVal, Action<float> settable, bool restore)
		{
			JSONStorableFloat settableVal = new JSONStorableFloat(name, initialVal, 0f, 1f);
			RegisterFloat(settableVal);
			settableVal.setJSONCallbackFunction += delegate (JSONStorableFloat js) { settable(js.val); };
			if (restore && pluginJson != null) settableVal.RestoreFromJSON(pluginJson);
			UIDynamicSlider solverPositionWeightslider = CreateSlider(settableVal, true);
			solverPositionWeightslider.labelText.text = displayName;
			return solverPositionWeightslider;
		}

		protected UIDynamic createIKToggle(string name, string displayName, bool initialVal, Action<bool> settable, bool restore)
		{
			JSONStorableBool solverFixTransforms = new JSONStorableBool(name, true);
			RegisterBool(solverFixTransforms);
			solverFixTransforms.setJSONCallbackFunction += (delegate (JSONStorableBool js) { settable(js.val); });
			if (restore && pluginJson!=null) solverFixTransforms.RestoreFromJSON(pluginJson);
			UIDynamicToggle tog = CreateToggle(solverFixTransforms, true);
			tog.labelText.text = displayName;
			return tog;
		}

		protected JSONStorableBool createIKToggleStorable(string name, bool initialVal, Action<bool> settable, bool restore)
		{
			JSONStorableBool solverFixTransforms = new JSONStorableBool(name, true);
			RegisterBool(solverFixTransforms);
			solverFixTransforms.setJSONCallbackFunction += (delegate (JSONStorableBool js) { settable(js.val); });
			if (restore && pluginJson != null) solverFixTransforms.RestoreFromJSON(pluginJson);
			return solverFixTransforms;
		}

		private void createIKPostAddUI(int solverId, SolverType sType, bool restore)
		{
			List<UIDynamic> uiDictL = new List<UIDynamic>();




			if (solverDict.ContainsKey(solverId))
			{
				VAMIKSolver solvD = solverDict[solverId];
				//solvD.forceEnable;
				uiDictL.Add(CreateLabel("Solver "+(solverId+1)+" "+sType.ToString(), true));

				solvD.forceEnable = createIKToggleStorable("enableSolver" + solverId,  solvD.solver.enabled, (bool val) => { if (solvD.headEffector != null) { solvD.headEffector.enabled = val; } solvD.solver.enabled = val;  }, restore);
				UIDynamicToggle tog = CreateToggle(solvD.forceEnable, true);
				tog.labelText.text = "Enabled";
				uiDictL.Add(tog);

				uiDictL.Add(createIKToggle("solverFixTransform" + solverId, "Fix Transforms",solvD.solver.fixTransforms, (bool val) => { solvD.solver.fixTransforms = val; }, restore));

				if(sType.Equals(SolverType.CCD))
				{
					CCDIK ccd = (CCDIK)solvD.solver;
					uiDictL.Add(createIKFloatSlider("solverPositionWeight" + solverId,"Position Weight", ccd.solver.IKPositionWeight, ccd.solver.SetIKPositionWeight, restore));
				}
				else if (sType.Equals(SolverType.FABBRIK))
				{
					FABRIK fab = (FABRIK)solvD.solver;
					uiDictL.Add(createIKFloatSlider("solverPositionWeight" + solverId, "Position Weight", fab.solver.IKPositionWeight, fab.solver.SetIKPositionWeight, restore));
				}
				else if (sType.Equals(SolverType.Limb))
				{
					LimbIK limb = (LimbIK)solvD.solver;
					uiDictL.Add(createIKFloatSlider("solverPositionWeight" + solverId, "Position Weight", limb.solver.IKPositionWeight, limb.solver.SetIKPositionWeight, restore));
					uiDictL.Add(createIKFloatSlider("solverRotationWeight" + solverId, "Rotation Weight", limb.solver.IKRotationWeight, limb.solver.SetIKRotationWeight, restore));
					uiDictL.Add(createIKFloatSlider("solverRotationWeightMaintain" + solverId, "Maintain Orig Rotation", limb.solver.maintainRotationWeight, (float val) => { limb.solver.maintainRotationWeight = val; }, restore));
				}
				else if (sType.Equals(SolverType.FullBody))
				{
					FullBodyBipedIK fbb = (FullBodyBipedIK)solvD.solver;
					FBBIKHeadEffector fbbH = null;
					LookAtIK lok = null;

					if (solvD.headEffector != null)
					{
						fbbH = (FBBIKHeadEffector)solvD.headEffector;
						lok = fbb.GetComponent<LookAtIK>();
					}
					uiDictL.Add(createIKFloatSlider("solverPositionWeight" + solverId, "Overall Position Weight", fbb.solver.IKPositionWeight, fbb.solver.SetIKPositionWeight, restore));

					if(fbbH!=null)
					{ 	
					//Head
					uiDictL.Add(CreateLabel("Head", true));
					uiDictL.Add(createIKToggle("lookAtToggle" + solverId, "Look At On/Off", lok.enabled, (bool val) => { lok.enabled = val; }, restore));					

					JSONStorableStringChooser lookAtTarget = new JSONStorableStringChooser("lookAtTarget"+counter, (new string[] { "None", "Target", "Player" }).ToList<string>(), "Target", "Look Target");
					RegisterStringChooser(lookAtTarget);

						if (restore && pluginJson != null)
						{
							lookAtTarget.RestoreFromJSON(pluginJson);
						};


						if (lookAtTarget.val.Equals("None"))
								lok.solver.target = null;
							else if (lookAtTarget.val.Equals("Target"))
								lok.solver.target = fbbH.transform;
							else if (lookAtTarget.val.Equals("Player"))
								lok.solver.target = SuperController.singleton.centerCameraTarget.transform;


					UIDynamicPopup fp = CreateFilterablePopup(lookAtTarget, true);
					fp.popupPanelHeight = 700f;
					uiDictL.Add(fp);
					fp.popup.onValueChangeHandlers += delegate (string val) 
					{
						if (val.Equals("None"))
							lok.solver.target = null;
						else if (val.Equals("Target"))
							lok.solver.target = fbbH.transform;
						else if (val.Equals("Player"))
							lok.solver.target = SuperController.singleton.centerCameraTarget.transform;
					};

					
						uiDictL.Add(createIKFloatSlider("effectorPositionWeight_Eye_" + solverId, "Eye Weight", lok.solver.eyesWeight, (float val) => { lok.solver.eyesWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorClampWeight_Eye_" + solverId, "Eye Clamp Weight", lok.solver.clampWeightEyes, (float val) => { lok.solver.clampWeightEyes = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorLookBodyWeight" + solverId, "Look Body Weight", lok.solver.bodyWeight, (float val) => { lok.solver.bodyWeight = val; }, restore));

					uiDictL.Add(createIKToggle("headEffectorToggle" + solverId, "Head Position On/Off", fbbH.enabled, (bool val) => { fbbH.enabled = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_Head_" + solverId, "Position Weight", fbbH.positionWeight, (float val) => { fbbH.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeight_Head_" + solverId, "Rotation Weight", fbbH.rotationWeight, (float val) => { fbbH.rotationWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorBendWeight_Head_" + solverId, "Neck Bend Weight", fbbH.bendWeight, (float val) => { fbbH.bendWeight = val; }, restore));
					uiDictL.Add(createIKToggle("handsPullBody" + solverId, "Hands Pull Body", fbbH.handsPullBody, (bool val) => { fbbH.handsPullBody = val; }, restore));
					}

					//Body Effector
					uiDictL.Add(CreateLabel("Spine", true));					
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_Body_" + solverId, "Position Weight", fbb.solver.bodyEffector.positionWeight, (float val) => { fbb.solver.bodyEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKToggle("useThighs" + solverId, "Use Thighs", fbb.solver.bodyEffector.effectChildNodes, (bool val) => { fbb.solver.bodyEffector.effectChildNodes = val; }, restore));

					//Left Arm
					uiDictL.Add(CreateLabel("Left Arm", true));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LShoulder_" + solverId, "Shoulder Position Weight", fbb.solver.leftShoulderEffector.positionWeight, (float val) => { fbb.solver.leftShoulderEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LElbow_" + solverId, "Elbow Position Weight", fbb.solver.leftArmChain.bendConstraint.weight, (float val) => { fbb.solver.leftArmChain.bendConstraint.weight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LHand_" + solverId, "Hand Position Weight", fbb.solver.leftHandEffector.positionWeight, (float val) => { fbb.solver.leftHandEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeight_LHand_" + solverId, "Hand Rotation Weight", fbb.solver.leftHandEffector.rotationWeight, (float val) => { fbb.solver.leftHandEffector.rotationWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeightMaintain_LHand_" + solverId, "Maintain Hand Rotation", fbb.solver.leftHandEffector.maintainRelativePositionWeight, (float val) => { fbb.solver.leftHandEffector.maintainRelativePositionWeight = val; }, restore));

					//Right Arm
					uiDictL.Add(CreateLabel("Right Arm", true));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RShoulder_" + solverId, "Shoulder Position Weight", fbb.solver.rightShoulderEffector.positionWeight, (float val) => { fbb.solver.rightShoulderEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RElbow_" + solverId, "Elbow Position Weight", fbb.solver.rightArmChain.bendConstraint.weight, (float val) => { fbb.solver.rightArmChain.bendConstraint.weight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RHand_" + solverId, "Hand Position Weight", fbb.solver.rightHandEffector.positionWeight, (float val) => { fbb.solver.rightHandEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeight_RHand_" + solverId, "Hand Rotation Weight", fbb.solver.rightHandEffector.rotationWeight, (float val) => { fbb.solver.rightHandEffector.rotationWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeightMaintain_RHand_" + solverId, "Maintain Hand Rotation", fbb.solver.rightHandEffector.maintainRelativePositionWeight, (float val) => { fbb.solver.rightHandEffector.maintainRelativePositionWeight = val; }, restore));

					//Left Leg
					uiDictL.Add(CreateLabel("Left Leg", true));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LThigh_" + solverId, "Thigh Position Weight", fbb.solver.leftThighEffector.positionWeight, (float val) => { fbb.solver.leftThighEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LKnee_" + solverId, "Knee Position Weight", fbb.solver.leftLegChain.bendConstraint.weight, (float val) => { fbb.solver.leftLegChain.bendConstraint.weight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_LFoot_" + solverId, "Foot Position Weight", fbb.solver.leftFootEffector.positionWeight, (float val) => { fbb.solver.leftFootEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeight_LFoot_" + solverId, "Foot Rotation Weight", fbb.solver.leftFootEffector.rotationWeight, (float val) => { fbb.solver.leftFootEffector.rotationWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeightMaintain_LFoot_" + solverId, "Maintain Foot Rotation", fbb.solver.leftFootEffector.maintainRelativePositionWeight, (float val) => { fbb.solver.leftFootEffector.maintainRelativePositionWeight = val; }, restore));

					//right Leg
					uiDictL.Add(CreateLabel("Right Leg", true));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RThigh_" + solverId, "Thigh Position Weight", fbb.solver.rightThighEffector.positionWeight, (float val) => { fbb.solver.rightThighEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RKnee_" + solverId, "Knee Position Weight", fbb.solver.rightLegChain.bendConstraint.weight, (float val) => { fbb.solver.rightLegChain.bendConstraint.weight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorPositionWeight_RFoot_" + solverId, "Foot Position Weight", fbb.solver.rightFootEffector.positionWeight, (float val) => { fbb.solver.rightFootEffector.positionWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeight_RFoot_" + solverId, "Foot Rotation Weight", fbb.solver.rightFootEffector.rotationWeight, (float val) => { fbb.solver.rightFootEffector.rotationWeight = val; }, restore));
					uiDictL.Add(createIKFloatSlider("effectorRotationWeightMaintain_RFoot_" + solverId, "Maintain Foot Rotation", fbb.solver.rightFootEffector.maintainRelativePositionWeight, (float val) => { fbb.solver.rightFootEffector.maintainRelativePositionWeight = val; }, restore));

				}
				uiDictL.Add(CreateSpacer(true));
				//limb bend goals
			}

			if (uiDict.ContainsKey(solverId))
				uiDict[solverId].AddRange(uiDictL);
			else
				uiDict.Add(solverId, uiDictL);
		}

		private JSONClass extractPluginJSON(JSONNode file, string id)
		{
			JSONClass retJson = null;

			JSONNode sceneFile = file.AsObject["atoms"];

			foreach (JSONNode st in sceneFile.Childs)
			{
				if (st["id"].ToString().Equals("\"" + id + "\""))
				{

					foreach (JSONNode subSt in st["storables"].Childs)
					{						
						if (subSt["id"].ToString().Equals("\"" + storeId + "\""))
						{
							retJson = subSt.AsObject;
							break;
						}
					}
					break;
				}
			}

			return retJson;
		}

		public override void PostRestore()
		{		
			pluginJson = null;

			if (!subscene)
			{
				pluginJson = extractPluginJSON(SuperController.singleton.loadJson, this.AtomUidToStoreAtomUid(this.containingAtom.uid));
				RestoreFromJSON((JSONClass)pluginJson);
			}
			else
			{

				JSONNode subsceneSave = SuperController.singleton.GetSaveJSON(this.containingAtom.parentAtom).AsObject["atoms"]; ;
				string ssPath = null;

				foreach (JSONNode st in subsceneSave.Childs)
				{
					if (st["id"].ToString().Equals("\"" + this.containingAtom.subScenePath.TrimEnd('/') + "\""))
					{
						foreach (JSONNode subSt in st["storables"].Childs)
						{
							if (subSt["id"].ToString().Equals("\"" + this.containingAtom.containingSubScene.storeId + "\""))
							{
								pluginJson = subSt.AsObject;
								ssPath = subSt["storePath"];
								break;
							}
						}
						break;
					}
				}

				if (ssPath != null && ssPath.Contains("/"))
				{
					try { 
					JSONNode subsceneNode = SuperController.singleton.LoadJSON(ssPath);
					pluginJson = extractPluginJSON(subsceneNode, this.AtomUidToStoreAtomUid(this.containingAtom.uid).Split('/')[1]);
					}catch (Exception e)
					{
						SuperController.LogError("Unable to load stored JSON: " + ssPath);
					}

					if (pluginJson != null)
						RestoreFromJSON((JSONClass)pluginJson);
				}

			}

			base.PostRestore();

			StartCoroutine(restoreScene());
		}

		protected Avatar createAvatar(Dictionary<string, HumanBodyBones> mapping, List<Transform> bones, GameObject root)
		{
			Avatar av = null;
			List<HumanBone> bonesH = new List<HumanBone>();
	
			Dictionary<HumanBodyBones, HumanBone> map = new Dictionary<HumanBodyBones, HumanBone>();
			foreach (KeyValuePair<string, HumanBodyBones> bon in mapping)
			{
				HumanBone hb = new HumanBone();
				string boneName = bon.Key;

				hb.boneName = boneName;
				hb.humanName = bon.Value.ToString();
				hb.limit.useDefaultValues = true;

				if (boneName.Length > 0)
				{
					map.Add(bon.Value, hb);
					bonesH.Add(hb);
				}
			}

			List<SkeletonBone> skeleton = new List<SkeletonBone>();			
			
			int x = 0;

			foreach (Transform bone in bones)
			{
				SkeletonBone sb = new SkeletonBone();
				sb.name = bone.name;
				sb.position = bone.localPosition;
				sb.rotation = bone.localRotation;
				sb.scale = bone.localScale;

				skeleton.Add(sb);
				x++;
			}

			HumanDescription hd = new HumanDescription();

			hd.human = bonesH.ToArray();
			hd.skeleton = skeleton.ToArray();
			//   hd.hasTranslationDoF = true;

			//set the default values for the rest of the human descriptor parameters
			hd.upperArmTwist = 0.5f;
			hd.lowerArmTwist = 0.5f;
			hd.upperLegTwist = 0.5f;
			hd.lowerLegTwist = 0.5f;
			hd.armStretch = 0.05f;
			hd.legStretch = 0.05f;
			hd.feetSpacing = 0.2f;

			av = AvatarBuilder.BuildHumanAvatar(root, hd);
			return av;
		}

	}
}
