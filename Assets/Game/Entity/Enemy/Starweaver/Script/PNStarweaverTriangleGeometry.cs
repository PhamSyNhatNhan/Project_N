using System;
// SAT on XZ: filled equilateral triangle against a target footprint, including boundary.
public static class PNStarweaverTriangleGeometry
{
    public static bool Intersects(float cx,float cz,float radius,float minX,float minZ,float maxX,float maxZ)
    {
        if(!(radius>0)||float.IsInfinity(radius))throw new ArgumentOutOfRangeException(nameof(radius));
        double ax=cx,az=cz+radius,bx=cx+radius*.866025403784,bz=cz-radius*.5,cx2=cx-radius*.866025403784,cz2=bz;
        return Axis(1,0,ax,az,bx,bz,cx2,cz2,minX,minZ,maxX,maxZ)&&Axis(0,1,ax,az,bx,bz,cx2,cz2,minX,minZ,maxX,maxZ)&&Axis(-(bz-az),bx-ax,ax,az,bx,bz,cx2,cz2,minX,minZ,maxX,maxZ)&&Axis(-(cz2-bz),cx2-bx,ax,az,bx,bz,cx2,cz2,minX,minZ,maxX,maxZ)&&Axis(-(az-cz2),ax-cx2,ax,az,bx,bz,cx2,cz2,minX,minZ,maxX,maxZ);
    }
    private static bool Axis(double x,double z,double ax,double az,double bx,double bz,double cx,double cz,float minX,float minZ,float maxX,float maxZ)
    {
        double a=x*ax+z*az,b=x*bx+z*bz,c=x*cx+z*cz;
        double mid=x*(minX+maxX)*.5+z*(minZ+maxZ)*.5,extent=Math.Abs(x)*(maxX-minX)*.5+Math.Abs(z)*(maxZ-minZ)*.5;
        return Math.Max(a,Math.Max(b,c))+1e-6>=mid-extent&&Math.Min(a,Math.Min(b,c))-1e-6<=mid+extent;
    }
}
