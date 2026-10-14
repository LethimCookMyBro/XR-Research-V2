using System;
using Vector3 = UnityEngine.Vector3;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1CablePath
    {
        public static Vector3[] Solve(Vector3 a, Vector3 b, Vector3 outwardA, Vector3 outwardB, double length, Vector3 side, double minimumRadius)
        {
            if (!Valid(a) || !Valid(b) || !Valid(outwardA) || !Valid(outwardB) || !Valid(side) || double.IsNaN(length) || double.IsInfinity(length) || length <= 0 ||
                double.IsNaN(minimumRadius) || double.IsInfinity(minimumRadius) || minimumRadius < 0 || Vector3.Distance(a,b) > length) return null;
            float an = Vector3.Distance(outwardA,new Vector3()), bn = Vector3.Distance(outwardB,new Vector3()), sn = Vector3.Distance(side,new Vector3());
            if (an < .00001f || bn < .00001f || sn < .00001f) return null;
            outwardA /= an; outwardB /= bn; side /= sn;
            foreach(float fraction in new[]{.25f,.15f,.08f,.04f})
            {
                float neck=(float)length*fraction;
                var path=Curve(a,b,outwardA,outwardB,side,0,neck);
                if(Length(path)>length)continue;
                double low=0,high=length*4;
                for(int i=0;i<20;i++)
                {
                    double middle=(low+high)/2;
                    path=Curve(a,b,outwardA,outwardB,side,(float)middle,neck);
                    if(Length(path)<length)low=middle;else high=middle;
                }
                path=Curve(a,b,outwardA,outwardB,side,(float)((low+high)/2),neck);
                if(Math.Abs(Length(path)-length)>.00001)continue;
                bool radiusValid=true;
                for(int i=1;i<path.Length-1;i++)
                {
                    double x=Vector3.Distance(path[i-1],path[i]),y=Vector3.Distance(path[i],path[i+1]),z=Vector3.Distance(path[i-1],path[i+1]);
                    double semi=(x+y+z)/2,areaSquared=semi*(semi-x)*(semi-y)*(semi-z);
                    if(areaSquared>1e-20 && x*y*z/(4*Math.Sqrt(areaSquared))<minimumRadius) {radiusValid=false;break;}
                }
                if(radiusValid)return path;
            }
            return null;
        }
        private static bool Valid(Vector3 p) => !float.IsNaN(Vector3.Dot(p,p)) && !float.IsInfinity(Vector3.Dot(p,p));
        private static Vector3[] Curve(Vector3 a,Vector3 b,Vector3 da,Vector3 db,Vector3 side,float height,float neck)
        {
            var p1=a+da*neck;var p2=(a+b)/2+side*height;var p3=b+db*neck;
            var path=new Vector3[65];
            for(int i=0;i<path.Length;i++)
            {
                float t=(float)i/(path.Length-1),u=1-t;
                path[i]=a*(u*u*u*u)+p1*(4*u*u*u*t)+p2*(6*u*u*t*t)+p3*(4*u*t*t*t)+b*(t*t*t*t);
            }
            return path;
        }
        public static double Length(Vector3[] path)
        { double length=0;for(int i=1;i<path.Length;i++)length+=Vector3.Distance(path[i-1],path[i]);return length; }
        public static void At(Vector3[] path,double fraction,out Vector3 position,out Vector3 direction)
        {
            double distance=Length(path)*fraction;
            for(int i=1;i<path.Length;i++)
            {
                double span=Vector3.Distance(path[i-1],path[i]);
                if(distance<=span || i==path.Length-1) {direction=path[i]-path[i-1];position=span<1e-12?path[i]:path[i-1]+direction*(float)(distance/span);return;}
                distance-=span;
            }
            position=path[path.Length-1];direction=path[path.Length-1]-path[path.Length-2];
        }
    }
}
