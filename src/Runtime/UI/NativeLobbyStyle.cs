using System;using System.Linq;using TMPro;using UnityEngine;using UnityEngine.UI;
namespace WKPmxAvatar
{
    // Borrow existing sprites/materials/font only. Never clone UI controllers or persistent listeners.
    internal sealed class NativeLobbyStyle
    {
        internal TMP_FontAsset Font;Image background,button,selection;Selectable sourceButton;
        internal string Description=>"Lobby avatar UI: font="+(Font!=null?Font.name:"default")+"; background="+(background!=null?background.name:"neutral fallback")+"; button="+(button!=null?button.name:"neutral fallback")+"; selection="+(selection!=null?selection.name:"outline fallback");
        internal NativeLobbyStyle(GameObject screen)
        {
            var texts=screen.GetComponentsInChildren<TMP_Text>(true);Font=texts.Select(t=>t.font).FirstOrDefault(f=>f!=null);
            var pane=screen.transform.Find("Lobby Pane");
            var template=pane?.Find("Tabs/Tab Buttons/ModeButton_Lobby")??pane?.Find("Tabs/Tab Buttons/ModeButton_Template");
            sourceButton=template?.GetComponent<Selectable>();button=sourceButton?.targetGraphic as Image;
            if(button==null&&template!=null)button=template.GetComponentsInChildren<Image>(true).FirstOrDefault(i=>i.sprite!=null&&i.color.a>.1f&&!i.name.Contains("Highlight"));
            var images=(pane??screen.transform).GetComponentsInChildren<Image>(true);
            background=images.FirstOrDefault(i=>i.transform==pane&&i.sprite!=null)??images.Where(i=>i.sprite!=null&&i.color.a>.1f&&(string.Equals(i.name,"Background",StringComparison.OrdinalIgnoreCase)||string.Equals(i.name,"Panel",StringComparison.OrdinalIgnoreCase))).OrderByDescending(i=>Mathf.Abs(i.rectTransform.rect.width*i.rectTransform.rect.height)).FirstOrDefault();
            selection=template?.GetComponentsInChildren<Image>(true).FirstOrDefault(i=>i.sprite!=null&&(i.name.IndexOf("Select",StringComparison.OrdinalIgnoreCase)>=0||i.name.IndexOf("Highlight",StringComparison.OrdinalIgnoreCase)>=0||i.name.IndexOf("Outline",StringComparison.OrdinalIgnoreCase)>=0));
        }
        static void Copy(Image source,Image target)
        {target.sprite=source.sprite;target.material=source.material;target.type=source.type==Image.Type.Filled?Image.Type.Simple:source.type;target.pixelsPerUnitMultiplier=source.pixelsPerUnitMultiplier;target.color=source.color;target.preserveAspect=false;}
        internal void Surface(Image target){if(background!=null)Copy(background,target);else target.color=new Color(.12f,.12f,.12f,.97f);}
        internal void Button(Button target)
        {
            var image=(Image)target.targetGraphic;if(button!=null)Copy(button,image);else image.color=new Color(.22f,.22f,.22f,.97f);
            if(sourceButton!=null)target.colors=sourceButton.colors;
        }
        internal void Mark(Button target,bool active)
        {
            if(!active)return;
            if(selection!=null){var go=new GameObject("NativeSelectionFrame",typeof(RectTransform),typeof(Image));go.layer=target.gameObject.layer;go.transform.SetParent(target.transform,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;var image=go.GetComponent<Image>();Copy(selection,image);image.raycastTarget=false;go.transform.SetAsFirstSibling();}
            else {var outline=target.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.85f,.85f,.85f,.9f);outline.effectDistance=new Vector2(1,-1);}
        }
    }
}
