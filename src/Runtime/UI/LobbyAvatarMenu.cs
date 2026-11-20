using System;using System.Linq;using TMPro;using UnityEngine;using UnityEngine.UI;using UnityEngine.Events;using HarmonyLib;
namespace WKPmxAvatar
{
    public sealed class LobbyAvatarMenu:MonoBehaviour
    {
        Plugin owner;TMP_FontAsset font;NativeLobbyStyle style;Component nativeAnimation;CanvasGroup nativeGroup,gate;
        UnityEvent shown,hidden;float exitUntil;GameObject hostPane,modal;RectTransform panel,detailsPanel,content;
        TMP_Text status,summary,detailsTitle,detailsInfo,detailsStatus,handToggle,pawToggle,materialToggle,lightingToggle;
#if WKPMX_DEV
        TMP_Text eyeToggle;
#endif
        GameObject rossiOptions;string pending,detailsId;
        static object Field(object instance,string name)=>instance==null?null:AccessTools.Field(instance.GetType(),name)?.GetValue(instance);
        bool AnimationOpen=>nativeAnimation!=null&&Field(nativeAnimation,"state") is bool state&&state;
        public static void Mount(GameObject screen,Plugin plugin)
        {
            if(screen.GetComponentInChildren<LobbyAvatarMenu>(true)!=null)return;
            var pane=screen.transform.Find("Lobby Pane");if(pane==null)return;
            var animator=pane.GetComponents<Component>().FirstOrDefault(c=>c!=null&&c.GetType().Name=="UI_LerpOpen");if(animator==null){plugin.SelectionWarning("Native lobby animation unavailable; avatar entry not mounted.");return;}
            // Inherit the original transform tween, canvas, fading and input layer.
            var root=new GameObject("WKPmx_AvatarSelection",typeof(RectTransform),typeof(CanvasGroup));root.layer=pane.gameObject.layer;root.transform.SetParent(pane,false);Stretch(root.GetComponent<RectTransform>());
            var menu=root.AddComponent<LobbyAvatarMenu>();menu.owner=plugin;menu.hostPane=pane.gameObject;menu.nativeAnimation=animator;menu.nativeGroup=Field(animator,"group") as CanvasGroup??pane.GetComponent<CanvasGroup>();menu.gate=root.GetComponent<CanvasGroup>();menu.gate.ignoreParentGroups=false;menu.gate.alpha=0;menu.gate.blocksRaycasts=menu.gate.interactable=false;
            menu.style=new NativeLobbyStyle(screen);menu.font=menu.style.Font;menu.Build();
            menu.shown=Field(animator,"eventOnShow") as UnityEvent;menu.hidden=Field(animator,"eventOnHide") as UnityEvent;menu.shown?.AddListener(menu.NativeShow);menu.hidden?.AddListener(menu.NativeHide);menu.SyncVisibility();plugin.InfoLog(menu.style.Description+"; anchored to native Lobby Pane animation");
        }
        static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        static RectTransform Box(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=pos;r.sizeDelta=size;return r;}
        TMP_Text Label(Transform parent,string text,int size=20)
        {var rect=Box("Label",parent,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);Stretch(rect);rect.offsetMin=new Vector2(12,5);rect.offsetMax=new Vector2(-12,-5);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.richText=false;label.fontSize=size;label.color=new Color(.94f,.94f,.95f);label.alignment=TextAlignmentOptions.MidlineLeft;label.raycastTarget=false;return label;}
        Button ButtonAt(Transform parent,string name,string text,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,Action click)
        {var rect=Box(name,parent,anchor,pivot,pos,size);var image=rect.gameObject.AddComponent<Image>();var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;style.Button(button);button.onClick.AddListener(()=>{try{click();}catch(Exception e){if(status!=null)status.text="Could not apply. Check BepInEx/LogOutput.log.";if(detailsStatus!=null)detailsStatus.text="Could not apply. Check BepInEx/LogOutput.log.";owner.SelectionWarning(e.Message);}});Label(rect,text);return button;}
        void Build()
        {
            ButtonAt(transform,"OpenAvatarPicker","AVATAR",Vector2.one,Vector2.one,new Vector2(-22,-20),new Vector2(220,44),Open);
            modal=Box("AvatarPickerOverlay",transform,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero).gameObject;Stretch(modal.GetComponent<RectTransform>());modal.AddComponent<Image>().color=new Color(0,0,0,.72f);
            panel=Box("AvatarPickerPanel",modal.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(620,690));style.Surface(panel.gameObject.AddComponent<Image>());
            Label(Box("Title",panel,Vector2.up,Vector2.up,new Vector2(14,-12),new Vector2(450,42)),"MY AVATAR",26);
            ButtonAt(panel,"Close","CLOSE",Vector2.one,Vector2.one,new Vector2(-16,-16),new Vector2(100,38),()=>modal.SetActive(false));
            Label(Box("Description",panel,Vector2.up,Vector2.up,new Vector2(14,-62),new Vector2(592,58)),"Choose how other players see you.\nThey need this addon version and the same model.",19);
            summary=Label(Box("CurrentSelection",panel,Vector2.up,Vector2.up,new Vector2(14,-124),new Vector2(592,40)),"",19);
            var list=Box("ModelList",panel,Vector2.up,Vector2.up,new Vector2(22,-182),new Vector2(576,334));style.Surface(list.gameObject.AddComponent<Image>());list.gameObject.AddComponent<RectMask2D>();var scroll=list.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=28;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.viewport=list;
            content=Box("Content",list,Vector2.up,Vector2.up,Vector2.zero,Vector2.zero);content.anchorMax=Vector2.one;content.anchorMin=Vector2.up;var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.padding=new RectOffset(8,8,8,8);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.content=content;
            status=Label(Box("Status",panel,Vector2.zero,Vector2.zero,new Vector2(14,84),new Vector2(592,56)),"",17);
            ButtonAt(panel,"Rescan","RESCAN",Vector2.zero,Vector2.zero,new Vector2(22,26),new Vector2(140,44),()=>{owner.RescanModels();Rebuild();status.text=owner.Models.Warnings.Count>0?string.Join("\n",owner.Models.Warnings.Take(2)):"Model library updated.";});
            ButtonAt(panel,"Details","DETAILS",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,26),new Vector2(160,44),OpenDetails);
            ButtonAt(panel,"Apply","APPLY",Vector2.right,Vector2.right,new Vector2(-22,26),new Vector2(140,44),()=>{owner.ChoosePersonalModel(pending);Rebuild();status.text="Saved. Open DETAILS for this model's options.";});
            BuildDetails();modal.SetActive(false);
        }
        void BuildDetails()
        {
            detailsPanel=Box("ModelDetailsPanel",modal.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(620,690));style.Surface(detailsPanel.gameObject.AddComponent<Image>());
            detailsTitle=Label(Box("Title",detailsPanel,Vector2.up,Vector2.up,new Vector2(14,-12),new Vector2(450,42)),"MODEL DETAILS",26);
            ButtonAt(detailsPanel,"Back","BACK",Vector2.one,Vector2.one,new Vector2(-16,-16),new Vector2(100,38),BackToModels);
            detailsInfo=Label(Box("ModelInfo",detailsPanel,Vector2.up,Vector2.up,new Vector2(14,-70),new Vector2(592,90)),"",19);
            rossiOptions=Box("RossiOptions",detailsPanel,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero).gameObject;Stretch(rossiOptions.GetComponent<RectTransform>());
            var hand=ButtonAt(rossiOptions.transform,"HandsToggle","",Vector2.up,Vector2.up,new Vector2(22,-190),new Vector2(576,48),()=>{if(!CanEditRossi())return;owner.SetFirstPersonHands(!owner.FirstPersonHands.Value);RefreshToggles();detailsStatus.text="Rossi hand preference saved.";});handToggle=hand.GetComponentInChildren<TMP_Text>();
            var paw=ButtonAt(rossiOptions.transform,"PawToggle","",Vector2.up,Vector2.up,new Vector2(22,-254),new Vector2(576,48),()=>{if(!CanEditRossi())return;owner.SetPawPrompts(!owner.PawPrompts.Value);RefreshToggles();detailsStatus.text="Rossi paw cursor preference saved.";});pawToggle=paw.GetComponentInChildren<TMP_Text>();

#if WKPMX_DEV
            var eye=ButtonAt(rossiOptions.transform,"EyeToggle","",Vector2.up,Vector2.up,new Vector2(22,-318),new Vector2(576,48),()=>{if(!CanEditRossi())return;owner.SetEyeTracking(!owner.EyeTrackingEnabled.Value);RefreshToggles();detailsStatus.text="Eye tracking saved for this client.";});eyeToggle=eye.GetComponentInChildren<TMP_Text>();
#endif

            float renderingY=318;
#if WKPMX_DEV
            renderingY=382;
#endif
            var material=ButtonAt(rossiOptions.transform,"MaterialToggle","",Vector2.up,Vector2.up,new Vector2(22,-renderingY),new Vector2(576,48),()=>{if(!CanEditRossi())return;owner.SetEndfield(!owner.EndfieldEnabled.Value);RefreshToggles();detailsStatus.text="Material preference saved. Local rendering assets are required.";});materialToggle=material.GetComponentInChildren<TMP_Text>();
            var lighting=ButtonAt(rossiOptions.transform,"LightingToggle","",Vector2.up,Vector2.up,new Vector2(22,-renderingY-64),new Vector2(576,48),()=>{if(!CanEditRossi())return;owner.SetSceneLighting(!owner.SceneLighting.Value);RefreshToggles();detailsStatus.text="Scene lighting preference saved. Requires r7 materials.";});lightingToggle=lighting.GetComponentInChildren<TMP_Text>();
            detailsStatus=Label(Box("DetailsStatus",detailsPanel,Vector2.zero,Vector2.zero,new Vector2(14,84),new Vector2(592,90)),"",18);
            ButtonAt(detailsPanel,"Done","DONE",Vector2.right,Vector2.right,new Vector2(-22,26),new Vector2(140,44),BackToModels);
            detailsPanel.gameObject.SetActive(false);
        }
        void BackToModels(){detailsPanel.gameObject.SetActive(false);panel.gameObject.SetActive(true);}
        bool CanEditRossi()=>detailsId==owner.PersonalModelId&&owner.RossiSelected;
        void OpenDetails()
        {
            if(pending!=owner.PersonalModelId){status.text="Apply the selected model before opening DETAILS.";return;}
            var entry=owner.Models.Find(pending);if(entry==null)return;detailsId=entry.Id;detailsTitle.text=entry.Name+" - DETAILS";
            bool rossi=ModelProfiles.SupportsRossiOptions(entry.Id);rossiOptions.SetActive(rossi);detailsInfo.text=rossi?"Appearance options for Rossi.\nAll preferences affect only this client.":"No additional settings are available for this model yet.";
            detailsStatus.text=rossi?"Preferences are remembered for Rossi.\nOther models use their own settings.":"This page is reserved for this model's own options.";
            RefreshToggles();panel.gameObject.SetActive(false);detailsPanel.gameObject.SetActive(true);Resize();
        }
        void Open(){if(!SyncVisibility())return;pending=owner.PersonalModelId;Rebuild();status.text="Choose and APPLY a model. Use DETAILS for its options.";detailsPanel.gameObject.SetActive(false);panel.gameObject.SetActive(true);modal.SetActive(true);Resize();}
        void RefreshToggles(){handToggle.text=HandAppearanceOptions.ToggleLabel(owner.FirstPersonHands.Value);pawToggle.text="Paw cursor: "+(owner.PawPrompts.Value?"ON":"OFF");
            materialToggle.text="Rossi r7 materials: "+(owner.EndfieldEnabled.Value?"ON":"OFF");
            lightingToggle.text="Scene lighting: "+(owner.SceneLighting.Value?"ON":"OFF")+(owner.EndfieldEnabled.Value?"":" (requires r7 materials)");
#if WKPMX_DEV
eyeToggle.text="Eye tracking (local view): "+(owner.EyeTrackingEnabled.Value?"ON":"OFF");
#endif
}
        void Resize(){if(panel==null)return;var root=(RectTransform)transform;var scale=Vector3.one*LobbyLayout.Scale(root.rect.width,root.rect.height);panel.localScale=detailsPanel.localScale=scale;}
        void Rebuild()
        {
            if(owner.Models.Find(pending)==null)pending=owner.PersonalModelId;
            foreach(Transform child in content){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            foreach(var entry in owner.Models.Entries){string id=entry.Id;bool selected=id==pending;var row=ButtonAt(content,"Model_"+id,(selected?"[X] ":"[ ] ")+entry.Name,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(550,48),()=>{pending=id;Rebuild();status.text="Click APPLY to save your selection.";});row.gameObject.AddComponent<LayoutElement>().preferredHeight=48;style.Mark(row,selected);}
            summary.text="Current: "+(owner.Models.Find(owner.PersonalModelId)?.Name??"Unavailable model (using fallback)");
        }
        float ExitDuration(){float duration=0;foreach(string field in new[]{"positionLerpTime","scaleLerpTime","fadeOutLerpTime"})if(Field(nativeAnimation,field) is float value&&float.IsFinite(value))duration=Mathf.Max(duration,value);return Mathf.Clamp(duration,0,10);}
        void NativeShow(){exitUntil=0;SyncVisibility();}
        void NativeHide(){exitUntil=Time.unscaledTime+ExitDuration();if(modal!=null)modal.SetActive(false);SyncVisibility();}
        bool SyncVisibility()
        {
            bool active=hostPane!=null&&hostPane.activeInHierarchy,open=AnimationOpen;
            bool visible=LobbyVisibility.DuringAnimation(active,open,exitUntil-Time.unscaledTime);
            bool interactive=visible&&open&&(nativeGroup==null||nativeGroup.interactable);
            gate.alpha=visible?1:0;gate.interactable=gate.blocksRaycasts=interactive;if(!interactive&&modal!=null)modal.SetActive(false);return interactive;
        }
        void OnDisable(){exitUntil=0;if(modal!=null)modal.SetActive(false);}
        void OnDestroy(){shown?.RemoveListener(NativeShow);hidden?.RemoveListener(NativeHide);}
        void LateUpdate(){if(SyncVisibility()&&modal!=null&&modal.activeSelf)Resize();}
    }
}
