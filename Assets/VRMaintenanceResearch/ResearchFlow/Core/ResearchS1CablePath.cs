using System;
using Vector3 = UnityEngine.Vector3;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1CablePath
    {
        static readonly float[] Necks = { .25f, .15f, .08f, .04f };

        public static Vector3[] Solve(Vector3 a, Vector3 b, Vector3 outwardA, Vector3 outwardB, double length, Vector3 side, double minimumRadius)
        {
            if (!Valid(a) || !Valid(b) || !Valid(outwardA) || !Valid(outwardB) || !Valid(side) || double.IsNaN(length) || double.IsInfinity(length) || length <= 0 ||
                double.IsNaN(minimumRadius) || double.IsInfinity(minimumRadius) || minimumRadius < 0 || Vector3.Distance(a,b) > length) return null;
            float an = Vector3.Distance(outwardA,new Vector3()), bn = Vector3.Distance(outwardB,new Vector3()), sn = Vector3.Distance(side,new Vector3());
            if (an < .00001f || bn < .00001f || sn < .00001f) return null;
            outwardA /= an; outwardB /= bn; side /= sn;
            // One buffer per solve: a fresh array per trial curve allocated ~30 KB per moving frame per cable.
            var path=new Vector3[65];
            foreach(float fraction in Necks)
            {
                float neck=(float)length*fraction;
                Curve(a,b,outwardA,outwardB,side,0,neck,path);
                if(Length(path)>length)continue;
                double low=0,high=length*4;
                for(int i=0;i<20;i++)
                {
                    double middle=(low+high)/2;
                    Curve(a,b,outwardA,outwardB,side,(float)middle,neck,path);
                    if(Length(path)<length)low=middle;else high=middle;
                }
                Curve(a,b,outwardA,outwardB,side,(float)((low+high)/2),neck,path);
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
        public static Vector3[] SolveGuided(Vector3 a, Vector3 b, Vector3 outwardA, Vector3 outwardB,
            double length, Vector3[] guides, Vector3 slackDirection, double minimumRadius)
        {
            if (!Valid(a) || !Valid(b) || !Valid(outwardA) || !Valid(outwardB) || !Valid(slackDirection) ||
                guides == null || guides.Length < 1 || guides.Length > 32 ||
                double.IsNaN(length) || double.IsInfinity(length) || length <= 0 ||
                double.IsNaN(minimumRadius) || double.IsInfinity(minimumRadius) || minimumRadius < 0 ||
                outwardA.sqrMagnitude < 1e-10f || outwardB.sqrMagnitude < 1e-10f ||
                slackDirection.sqrMagnitude < 1e-10f || Vector3.Distance(a, b) > length)
                return null;
            var knots = new Vector3[guides.Length + 2];
            knots[0] = a; knots[knots.Length - 1] = b;
            for (int i = 0; i < guides.Length; i++)
            {
                if (!Valid(guides[i])) return null;
                knots[i + 1] = guides[i];
            }
            for (int i = 1; i < knots.Length; i++)
                if (Vector3.Distance(knots[i - 1], knots[i]) < .0001f) return null;
            var tangents = new Vector3[knots.Length];
            var path = new Vector3[(knots.Length - 1) * 64 + 1];
            foreach (float tension in new[] { .5f, .65f, .8f, .35f, 1f, .2f, .1f })
            {
                tangents[0] = outwardA.normalized * Vector3.Distance(knots[0], knots[1]) * tension;
                tangents[tangents.Length - 1] = -outwardB.normalized *
                    Vector3.Distance(knots[knots.Length - 2], b) * tension;
                for (int i = 1; i < knots.Length - 1; i++)
                    tangents[i] = (knots[i + 1] - knots[i - 1]).normalized * tension *
                        Math.Min((float)length / (16 * tension), Math.Min(Vector3.Distance(knots[i - 1], knots[i]), Vector3.Distance(knots[i], knots[i + 1])));
                GuidedCurve(knots, tangents, slackDirection.normalized, 0, path);
                double previous = Length(path) - length;
                if (Math.Abs(previous) <= 1e-5 && GuidedRadiusValid(path, minimumRadius)) return path;
                double low = 0, high = 0;
                bool bracketed = false;
                for (int sample = 1; sample <= 64; sample++)
                {
                    high = length * 2 * sample / 64;
                    GuidedCurve(knots, tangents, slackDirection.normalized, (float)high, path);
                    double current = Length(path) - length;
                    if (previous * current <= 0) { bracketed = true; break; }
                    previous = current; low = high;
                }
                if (!bracketed) continue;
                for (int iteration = 0; iteration < 24; iteration++)
                {
                    double middle = (low + high) / 2;
                    GuidedCurve(knots, tangents, slackDirection.normalized, (float)middle, path);
                    double value = Length(path) - length;
                    if (previous * value <= 0) high = middle;
                    else { low = middle; previous = value; }
                }
                GuidedCurve(knots, tangents, slackDirection.normalized, (float)((low + high) / 2), path);
                if (Math.Abs(Length(path) - length) <= 1e-4 && GuidedRadiusValid(path, minimumRadius)) return path;
            }
            return null;
        }

        static void GuidedCurve(Vector3[] knots, Vector3[] tangents, Vector3 side, float slack, Vector3[] path)
        {
            int index = 0;
            for (int segment = 0; segment < knots.Length - 1; segment++)
            {
                Vector3 p0 = knots[segment], p1 = p0 + tangents[segment];
                Vector3 p3 = knots[segment + 1], p2 = p3 - tangents[segment + 1];
                for (int sample = 0; sample < 64; sample++)
                {
                    float t = sample / 64f, u = 1 - t;
                    path[index++] = p0 * (u * u * u) + p1 * (3 * u * u * t) +
                        p2 * (3 * u * t * t) + p3 * (t * t * t) + side * (slack * 16 * t * t * u * u);
                }
            }
            path[index] = knots[knots.Length - 1];
        }

        static bool GuidedRadiusValid(Vector3[] path, double minimumRadius)
        {
            for (int i = 1; i < path.Length - 1; i++)
            {
                if (!Valid(path[i])) return false;
                double x = Vector3.Distance(path[i - 1], path[i]);
                double y = Vector3.Distance(path[i], path[i + 1]);
                double z = Vector3.Distance(path[i - 1], path[i + 1]);
                if (x < 1e-8 || y < 1e-8) return false;
                if (Vector3.Dot(path[i] - path[i - 1], path[i + 1] - path[i]) < 0) return false;
                double semi = (x + y + z) / 2, areaSquared = semi * (semi - x) * (semi - y) * (semi - z);
                if (areaSquared > 1e-20 && x * y * z / (4 * Math.Sqrt(areaSquared)) < minimumRadius)
                    return false;
            }
            return true;
        }

        private static bool Valid(Vector3 p) => !float.IsNaN(Vector3.Dot(p,p)) && !float.IsInfinity(Vector3.Dot(p,p));
        private static void Curve(Vector3 a,Vector3 b,Vector3 da,Vector3 db,Vector3 side,float height,float neck,Vector3[] path)
        {
            var p1=a+da*neck;var p2=(a+b)/2+side*height;var p3=b+db*neck;
            for(int i=0;i<path.Length;i++)
            {
                float t=(float)i/(path.Length-1),u=1-t;
                path[i]=a*(u*u*u*u)+p1*(4*u*u*u*t)+p2*(6*u*u*t*t)+p3*(4*u*t*t*t)+b*(t*t*t*t);
            }
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
