using UnityEngine;
using UnityEngine.UI;

public static class TutorialPhotoUI
{
    public static RawImage Create(Transform parent,string name,string photo,Vector2 min,Vector2 max)
    {
        var frame=RacingUITheme.Rect(parent,name,min,max);frame.gameObject.AddComponent<RectMask2D>();
        var content=RacingUITheme.Rect(frame,"Photograph",Vector2.zero,Vector2.one);
        var image=content.gameObject.AddComponent<RawImage>();image.texture=Resources.Load<Texture2D>("UI/TutorialPhotos/"+photo);image.raycastTarget=false;
        var fit=content.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio=image.texture!=null?(float)image.texture.width/image.texture.height:16f/9f;return image;
    }
}
