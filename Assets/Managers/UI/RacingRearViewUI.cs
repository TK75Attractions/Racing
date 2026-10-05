using UnityEngine;
using UnityEngine.UI;

/// <summary>Small rear-facing camera per player, with a chamfered mask and neon border.</summary>
public sealed class RacingRearViewUI : MonoBehaviour
{
    private int player;
    private Camera rearCamera;
    private RenderTexture target;
    private RawImage image;
    private Transform car;
    public void Configure(int index)
    {
        player=index;
        RectTransform crop=RacingUITheme.Rect(transform,"Crop",new Vector2(.008f,.045f),new Vector2(.992f,.955f));
        RacingPanelGraphic silhouette=crop.GetComponent<RacingPanelGraphic>() ?? crop.gameObject.AddComponent<RacingPanelGraphic>();
        silhouette.Configure(RacingPanelGraphic.SurfaceStyle.Panel,NeonUI.Cyan);
        Mask mask=crop.GetComponent<Mask>() ?? crop.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
        RectTransform r=RacingUITheme.Rect(crop,"Feed",Vector2.zero,Vector2.one);
        image=r.GetComponent<RawImage>() ?? r.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
        image.color=Color.clear;image.uvRect=new Rect(1,0,-1,1);
    }
    private void LateUpdate()
    {
        Transform next=Gmanager.Control?.GetPlayerCarTransform(player);
        if(next==null){if(rearCamera!=null)rearCamera.enabled=false;return;}
        if(car!=next || rearCamera==null)
        {
            Release();car=next;
            target=new RenderTexture(512,144,16,RenderTextureFormat.ARGB32);target.name="Rear view P"+(player+1);target.Create();image.texture=target;image.color=Color.white;
            GameObject go=new GameObject("RearViewCamera_P"+(player+1));go.transform.SetParent(car,false);
            go.transform.localPosition=new Vector3(0,1.7f,-1.4f);go.transform.localRotation=Quaternion.Euler(0,180,0);
            rearCamera=go.AddComponent<Camera>();rearCamera.targetTexture=target;rearCamera.fieldOfView=65;
            rearCamera.nearClipPlane=.3f;rearCamera.farClipPlane=500;rearCamera.allowHDR=false;rearCamera.allowMSAA=false;
            rearCamera.cullingMask=~(1<<gameObject.layer);rearCamera.depth=-10;rearCamera.aspect=512f/144f;
        }
        rearCamera.enabled=true;
    }
    private void OnDisable(){if(rearCamera!=null)rearCamera.enabled=false;}
    private void OnDestroy()=>Release();
    private void Release()
    {
        if(rearCamera!=null)Destroy(rearCamera.gameObject);rearCamera=null;
        if(target!=null){target.Release();Destroy(target);}target=null;car=null;
    }
}
