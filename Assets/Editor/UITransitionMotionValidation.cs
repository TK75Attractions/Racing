#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class UITransitionMotionValidation
{
    public static void Run()
    {
        UIInteractionMotionValidation.Run();
        var root=new GameObject("Shutter QA",typeof(RectTransform));
        try
        {
            var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(1920,1080);
            var wipe=root.AddComponent<RacingTransitionGraphic>();
            var populate=typeof(RacingTransitionGraphic).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(VertexHelper)},null);
            using(var vh=new VertexHelper())
            {
                wipe.SetCoverage(0,false);populate.Invoke(wipe,new object[]{vh});UIMotionValidation.Require(vh.currentVertCount==0,"Open shutters draw nothing.");
                wipe.SetCoverage(.5f,false);populate.Invoke(wipe,new object[]{vh});UIVertex middle=new UIVertex();
                vh.PopulateUIVertex(ref middle,2);float firstEdge=middle.position.x;
                UIMotionValidation.Require(firstEdge>rect.rect.xMin+rect.rect.width*.2f&&firstEdge<rect.rect.xMax-rect.rect.width*.1f,"At half-time, the leading shutter remains visibly within the screen.");
                vh.PopulateUIVertex(ref middle,4*12+2);
                UIMotionValidation.Require(middle.position.x<firstEdge,"Following bands must visibly trail the leader.");
                foreach(bool reveal in new[]{false,true})
                {
                    wipe.SetCoverage(1,reveal);populate.Invoke(wipe,new object[]{vh});UIVertex v=new UIVertex();
                    for(int i=0;i<5;i++)
                    {
                        vh.PopulateUIVertex(ref v,i*4);UIMotionValidation.Require(Mathf.Abs(v.position.x-rect.rect.xMin)<.01f,"Every covered band reaches the left edge.");
                        vh.PopulateUIVertex(ref v,i*4+2);UIMotionValidation.Require(Mathf.Abs(v.position.x-rect.rect.xMax)<.01f,"Every covered band reaches the right edge.");
                    }
                    wipe.SetCoverage(.45f,reveal);populate.Invoke(wipe,new object[]{vh});
                    for(int i=0;i<vh.currentVertCount;i++){vh.PopulateUIVertex(ref v,i);UIMotionValidation.Require(!float.IsNaN(v.position.x)&&v.position.x>=rect.rect.xMin&&v.position.x<=rect.rect.xMax,"Moving shutters stay within the viewport.");}
                }
            }
        }
        finally{Object.DestroyImmediate(root);}
        Debug.Log("UI_TRANSITION_GEOMETRY_PASS: complete coverage, geometric reveal, clipped moving bands and no idle geometry.");
    }
}
#endif
