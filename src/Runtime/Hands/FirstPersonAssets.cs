using System;using System.Collections.Generic;using System.IO;using UnityEngine;using Object=UnityEngine.Object;
namespace WKPmxAvatar
{
    // Sprite-only resources. No animation, interaction, input or inventory logic.
    public sealed class FirstPersonAssets:IDisposable
    {
        public static readonly string[] Poses={"normal","open","grab","crouch","sprint"};
        readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
        readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
        readonly Dictionary<Sprite,Sprite> originals=new Dictionary<Sprite,Sprite>();
        readonly List<Object> owned=new List<Object>();readonly Sprite pawOpen,pawGrip;
        public FirstPersonAssets(string folder)
        {
            try{
                foreach(string pose in Poses){var bytes=File.ReadAllBytes(Path.Combine(folder,pose+".png"));if(bytes.Length>16*1024*1024)throw new InvalidDataException("Hand texture size cap");
                    var image=new Texture2D(2,2,TextureFormat.RGBA32,false);owned.Add(image);if(!ImageConversion.LoadImage(image,bytes)||image.width>2048||image.height>2048)throw new InvalidDataException("Hand PNG invalid");image.name="RossiHand_"+pose;image.wrapMode=TextureWrapMode.Clamp;image.filterMode=FilterMode.Bilinear;textures[pose]=image;
                    var mirrored=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);owned.Add(mirrored);var pixels=image.GetPixels32();var flip=new Color32[pixels.Length];for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)flip[y*image.width+x]=pixels[y*image.width+image.width-1-x];mirrored.SetPixels32(flip);mirrored.Apply();mirrored.wrapMode=TextureWrapMode.Clamp;mirrored.filterMode=FilterMode.Bilinear;textures[pose+"-mirror"]=mirrored;
                }
                pawOpen=MakePaw(false);pawGrip=MakePaw(true);
            }catch{Dispose();throw;}
        }
        public Sprite Replace(string pose,Sprite original,bool mirrored,float scale)
        {
            if(original==null||!textures.TryGetValue(pose+(mirrored?"-mirror":""),out var image))return original;
            if(IsHand(original))return original;
            if(!float.IsFinite(scale))scale=0.85f;scale=Mathf.Clamp(scale,0.55f,1.15f);scale=Mathf.Round(scale*40)/40;
            string key=pose+":"+original.GetInstanceID()+":"+mirrored+":"+scale;
            if(sprites.TryGetValue(key,out var sprite))return sprite;if(sprites.Count>=512)return original;
            var rect=original.rect;var pivot=new Vector2(original.pivot.x/rect.width,original.pivot.y/rect.height);
            float ppu=Mathf.Max(image.width/(rect.width/original.pixelsPerUnit*scale),image.height/(rect.height/original.pixelsPerUnit*scale));
            sprite=Sprite.Create(image,new Rect(0,0,image.width,image.height),pivot,ppu,0,SpriteMeshType.FullRect);sprite.name="RossiFP/"+pose;owned.Add(sprite);sprites[key]=sprite;originals[sprite]=original;return sprite;
        }
        public bool IsHand(Sprite s)=>s!=null&&originals.ContainsKey(s);
        public Sprite Original(Sprite s)=>s!=null&&originals.TryGetValue(s,out var original)?original:s;
        public bool IsPaw(Sprite s)=>s!=null&&(s==pawOpen||s==pawGrip);
        public Sprite Paw(bool gripping)=>gripping?pawGrip:pawOpen;
        Sprite MakePaw(bool filled)
        {
            const int n=128;var pixels=new Color32[n*n];
            // Rounded cat-like toe pads and a soft three-lobed main pad; no claw spikes.
            bool ellipse(float x,float y,float cx,float cy,float rx,float ry){float a=(x-cx)/rx,b=(y-cy)/ry;return a*a+b*b<=1;}
            bool mainPad(float x,float y)=>ellipse(x,y,64,53,22,23)||ellipse(x,y,44,34,20,18)||ellipse(x,y,84,34,20,18)||ellipse(x,y,64,36,28,15);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                int hits=0;const int samples=4;for(int sy=0;sy<samples;sy++)for(int sx=0;sx<samples;sx++){
                    float px=x+(sx+0.5f)/samples,py=y+(sy+0.5f)/samples;
                    bool toe=ellipse(px,py,25,80,11,15)||ellipse(px,py,49,96,11,16)||ellipse(px,py,79,96,11,16)||ellipse(px,py,103,80,11,15);
                    bool pad=mainPad(px,py);if(!filled&&mainPad((px-64)/0.73f+64,(py-43)/0.73f+43))pad=false;
                    if(toe||pad)hits++;
                }
                pixels[y*n+x]=new Color32(255,255,255,(byte)(255*hits/(samples*samples)));
            }
            var t=new Texture2D(n,n,TextureFormat.RGBA32,false);owned.Add(t);t.SetPixels32(pixels);t.Apply();t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;
            var s=Sprite.Create(t,new Rect(0,0,n,n),new Vector2(0.5f,0.5f),128,0,SpriteMeshType.FullRect);owned.Add(s);s.name=filled?"RossiPaw/held":"RossiPaw/ready";return s;
        }
        public void Dispose(){for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null){if(Application.isPlaying)Object.Destroy(owned[i]);else Object.DestroyImmediate(owned[i]);}owned.Clear();sprites.Clear();textures.Clear();originals.Clear();}
    }
}
