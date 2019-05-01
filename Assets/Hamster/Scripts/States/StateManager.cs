// Copyright 2017 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using customNetwork;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Hamster.States {

  // Class for handling states and transitions between them.
  // Program flow is handled through state classes instead of
  // scene changes, because we want to preserve the objects
  // currently in the scene.
  public class StateManager {
    Stack<BaseState> stateStack;

    // Constructor.  Note that there is always at least one
    // state in the stack.  (By default, a base-state that does
    // nothing.)
    public StateManager() {
      stateStack = new Stack<BaseState>();
      stateStack.Push(new BaseState());
    }

    // Pushes a state onto the stack.  Suspends whatever is currently
    // running, and starts the new state up.  If this is being called
    // from a state's update() function, then in general it should be
    // at the end.  (Because calling this triggers suspend() and could
    // lead to weird situations if the state was planning on doing
    // more work.)
    public void PushState(BaseState newState) {
      newState.manager = this;
            BaseState curState = CurrentState();
      CurrentState().Suspend();
      stateStack.Push(newState);
      newState.Initialize(curState);    //  this calls both. It's up to the developer to only use ONE of these.
      newState.Initialize();    //  this second one is kept for backward compatibility reasons.
    }

    // Ends the currently-running state, and resumes whatever is next
    // down the line.
    public void PopState() {
      StateExitValue result = CurrentState().Cleanup();
      stateStack.Pop();
      CurrentState().Resume(result);
    }

    // Clears out all states, leaving just newState as the sole state
    // on the stack.  Since PopState is called, all underlying states
    // still get to respond to Resume() and Cleanup().  Mainly useful
    // for soft resets where we don't want to care about how many levels
    // of menu we have below us.
    public void ClearStack(BaseState newState) {
      while (stateStack.Count > 1) {
        PopState();
      }
      SwapState(newState);
    }

    // Switches the current state for a new one, without disturbing
    // anything below.  Different from Pop + Push, in that the next
    // state down never gets resumed/suspended.
    public void SwapState(BaseState newState) {
      newState.manager = this;
      CurrentState().Cleanup();
      stateStack.Pop();
      stateStack.Push(newState);
      CurrentState().Initialize();
    }

        static bool ClientOpenMatchFoundDetected = false;
        private static String CalcAutoguiString(BaseState curState)
        {
            if (curState is ClientLoadingLevel)
            {
                if (CommonData.mainGame.stateManager.CurrentState() is LevelFinished)
                {
                    ClientOpenMatchFoundDetected = false;   // reset this jiggery-pokery
                    return String.Empty;
                }

                if(ClientOpenMatchFoundDetected)
                {
                    return "Go!\r\nIn Open Match";
                }

                return "Lobby";
            }


            if (curState is ClientOpenMatchStart)
            {
                return "Starting Momentarily\r\nJoining Open Match";
            }

            if(curState is ClientOpenMatchFound)
            {
                ClientOpenMatchFoundDetected = true;
                return "Found Open Match";
            }

            return "";
        }

        static Menus.SingleLabelGUI autoGui;
        static int autoGuiStartCount;
        static string lastAutoGuiText = String.Empty;
        private static void UpdateAutogui(BaseState curState)
        {
            if (curState != null)// && curState.gui == null)
            {
                String str = CalcAutoguiString(curState);
                if (autoGui == null)
                {
                    //Debug.Log("UpdateAutogui: Started " + str + " #" + autoGuiStartCount++.ToString());
                    autoGui = s_SpawnSimpleLabelUI(str);
                    if (autoGui == null)
                    {
                        return;
                    }
                }
                if (lastAutoGuiText != str)
                {
                    Debug.Log("UpdateAutogui: Transitioned from " + lastAutoGuiText + " to " + str);
                    autoGui.LabelText.text = lastAutoGuiText = str;
                }
            }
            else if (autoGui != null)
            {
                lastAutoGuiText = autoGui.LabelText.text = "...";
            }
        }
		
        private static void s_SetupLabel(ref Menus.SingleLabelGUI menuComponent, string text)
        {
            if (menuComponent != null)
            {
                menuComponent.LabelText.fontSize /= 2;
                menuComponent.LabelText.text = text;

                VerticalLayoutGroup vlg = menuComponent.LabelText.GetComponentInParent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperLeft;

                Image img = menuComponent.LabelText.GetComponentInParent<Image>();
                if (img != null)
                {
                    img.color = Color.clear;
                }
            }
        }

        private static void s_PositionLabel(ref Menus.SingleLabelGUI menuComponent)
        {
            // adapt the resize hackery from Gameplay.PositionButton()
            GameObject resizeObj = menuComponent.LabelText.transform.parent.gameObject;
            RectTransform rt = resizeObj.GetComponent<RectTransform>();
            Vector2 localLowerLeft = rt.anchorMin + rt.offsetMin;
            Vector2 localUpperRight = rt.anchorMax + rt.offsetMax;
            Camera camera = CommonData.mainCamera.GetComponentInChildren<Camera>();
            // Locations of the corners of the button, in screen space.
            Vector2 screenLowerLeft =
                camera.WorldToScreenPoint(rt.TransformPoint(localLowerLeft));
            Vector2 screenUpperRight =
                camera.WorldToScreenPoint(rt.TransformPoint(localUpperRight));

            Vector2 localDimension = new Vector2(
                Mathf.Abs(localUpperRight.x - localLowerLeft.x) * 0.5f,
                Mathf.Abs(localUpperRight.y - localLowerLeft.y));

            float pixelsToLocalUnits = localDimension.x /
                Mathf.Abs(screenLowerLeft.x - screenUpperRight.x);

            // Move back button to upper right corner
            resizeObj.transform.localPosition = new Vector3(
                resizeObj.transform.localPosition.x +
                (Screen.width * pixelsToLocalUnits - localDimension.x) / 2.0f,
                resizeObj.transform.localPosition.y +
                (Screen.height * pixelsToLocalUnits - localDimension.y) / 2.0f,
                resizeObj.transform.localPosition.z);
        }

        // Depth to spawn UI, when running in mobile mode.
        const float UIDepthMobile = 6.0f;
        // Depth to spawn UI, when running in VR mode.
        const float UIDepthVR = 10.0f;

        private static Menus.SingleLabelGUI s_SpawnSimpleLabelUI(string text)
        {
            if (CommonData.prefabs == null || CommonData.prefabs.menuLookup == null || !CommonData.prefabs.menuLookup.ContainsKey(StringConstants.PrefabsSingleLabelMenu))
            {
                Debug.LogWarning("UpdateAutogui s_SpawnSimpleLabelUI: Failed with " + text);
                return null;
            }
            GameObject gui = GameObject.Instantiate(CommonData.prefabs.menuLookup[StringConstants.PrefabsSingleLabelMenu]);
            gui.transform.position = new Vector3(0, 0, CommonData.inVrMode ? UIDepthVR : UIDepthMobile);
            gui.transform.SetParent(CommonData.mainCamera.transform, false);
            Menus.SingleLabelGUI menuComponent = gui.GetComponent< Menus.SingleLabelGUI>();
            s_SetupLabel(ref menuComponent, text);
            s_PositionLabel(ref menuComponent);
            return menuComponent;
        }
    // Called by the main game every update.
    public void Update() {
      CurrentState().Update();
      if (MultiplayerGame.instance != null && this == MultiplayerGame.instance.clientStateManager)
      {
        UpdateAutogui(CurrentState());
      }
    }

    // Called by the main game every fixed update.
    // Note that during most UI and menus, the update timestep
    // is set to 0, so this function will not fire.
    public void FixedUpdate() {
      CurrentState().FixedUpdate();
    }

    // Called by the main game every UI update.
    public void OnGUI() {
      CurrentState().OnGUI();
    }

    // Handy utility function for checking the top state in the stack.
    public BaseState CurrentState() {
      return stateStack.Peek();
    }

    // When GUIButton receives a Unity UI event, it reports it via
    // this function.  (Which then directs it to whichever state is active.)
    public void HandleUIEvent(GameObject source, object eventData) {
      CurrentState().HandleUIEvent(source, eventData);
    }
  }

}
