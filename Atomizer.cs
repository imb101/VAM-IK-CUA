using System;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Blazedust.CUAM
{
	public class Atomizer
	{
		MonoBehaviour core;
		private Queue<KeyValuePair<Atom, Action<Atom>>> atomCreatedQueue;

		public Atomizer(MonoBehaviour script)
        {
			core = script;
		}

		public void Destroy()
		{
			if (atomCreatedQueue != null)
            {
				atomCreatedQueue.Clear();
				atomCreatedQueue = null;
			}
			core = null;
		}


		/// <summary>
		/// Creates an atom async and runs the onAtomCreated function once created in the next update cycle (requires Atomizer.Update() to be run each update).
		/// </summary>
		/// <param name="atomType"></param>
		/// <param name="atomUid"></param>
		/// <param name="atomPostfixIfExist"></param>
		/// <param name="onAtomCreated"></param>
		public void CreateAtom(string atomType, string atomUid, string atomPostfixIfExist, Action<Atom> onAtomCreated)
		{
			core.StartCoroutine(Create(atomType, atomUid, atomPostfixIfExist, onAtomCreated));
		}

		/// <summary>
		/// Creates an atom async and runs the onAtomCreated function once created in the next update cycle (requires Atomizer.Update() to be run each update).
		/// </summary>
		/// <param name="atomType"></param>
		/// <param name="atomUid"></param>
		/// <param name="atomPostfixIfExist"></param>
		/// <param name="onAtomCreated"></param>
		private IEnumerator Create(string atomType, string atomuid, string atomPostfixIfExist, Action<Atom> onAtomCreated)
		{
			yield return new WaitForSeconds(0.2f);

			string atomId = atomuid;
			Atom atom = SuperController.singleton.GetAtomByUid(atomId);
			if (atom != null && atomPostfixIfExist != null && atomPostfixIfExist != "")
			{
				// If atom by uid already exist, add postfix
				atomId = atomuid + "_" + atomPostfixIfExist;
				atom = SuperController.singleton.GetAtomByUid(atomId);
			}
			// if it still exist, keep adding sequence nr to it! Max 99, then we recycle the last added atom over-and-over...
			string unsequencedAtomId = atomId;
			int sequence = 2;
			while (atom != null && sequence < 99)
			{
				atomId = (unsequencedAtomId + "#" + sequence.ToString());
				sequence++;
				atom = SuperController.singleton.GetAtomByUid(atomId);
			}

			if (atom == null)
			{
				yield return SuperController.singleton.AddAtomByType(atomType, atomId);
				atom = SuperController.singleton.GetAtomByUid(atomId);
			}

			if (atom != null)
			{
				if (atomCreatedQueue == null)
                {
					atomCreatedQueue = new Queue<KeyValuePair<Atom, Action<Atom>>>();
				}
				atomCreatedQueue.Enqueue(new KeyValuePair<Atom, Action<Atom>>(atom, onAtomCreated));
			}
		}

		/// <summary>
		/// Handles any onAtomCreated Actions in the main update loop. Returns true if anything was consumed.
		/// </summary>
		public bool Update()
        {
			if (atomCreatedQueue != null && atomCreatedQueue.Count > 0)
            {
				KeyValuePair<Atom, Action<Atom>> p = atomCreatedQueue.Dequeue();
				if (!p.Key.destroyed)
				{
					p.Value(p.Key);
					return true;
				}
			}
			return false;
        }

	}
}