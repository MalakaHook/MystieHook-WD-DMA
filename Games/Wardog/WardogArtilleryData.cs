using System.Numerics;
namespace MystieHook.Games.Wardog;
// Calibrations, location labels and game-only tables ported from supplied MapData.h.
public static class WardogArtilleryData
{
 public readonly record struct Point(double X,double Y);
 public sealed record Location(Point Point,string Label);
 public sealed record Map(string Id,string Name,double OffsetX,double OffsetY,double MinX,double MaxX,double MinY,double MaxY,Location[] Locations,Point[][] Polygons);
 public sealed record Weapon(string Name,double Min,double Max,Point[] Low,Point[] High);
 public static readonly Map[] Maps = [
new("bakurani","Bakurani",-16320,-4080,-0.03,163.81,-0.01,163.83,[new(new(83.64,72.85),"Tower 4"),new(new(77.19,73.44),"Tower 3"),new(new(77.19,70.0),"Tower 2"),new(new(80.52,69.85),"Tower 1"),new(new(82.22,68.41),"Tower 5"),new(new(118.75,70.93),"Valkyra"),new(new(118.3,70.73),"Weapons Vendor"),new(new(118.14,70.45),"Garage Vendor"),new(new(118.37,70.49),"Spawn Board"),new(new(40.09,77.52),"Manticore"),new(new(39.77,77.65),"Weapons Vendor"),new(new(40.1,77.31),"Garage Vendor"),new(new(39.68,77.48),"Spawn Board"),new(new(87.46,32.5),"Lonestar"),new(new(87.2,32.68),"Weapons Vendor"),new(new(86.84,32.64),"Garage Vendor"),new(new(87.06,32.72),"Spawn Board")],[[new(117.5,73.76),new(121.22,70.71),new(118.18,66.99),new(114.45,70.04)],[new(38.68,79.88),new(43.39,78.85),new(42.35,74.15),new(37.65,75.18)],[new(83.08,35.27),new(87.72,36.51),new(88.97,31.86),new(84.32,30.62)]]),
new("ozeti","Ozeti",-16160,160,-0.03,163.81,-0.01,163.83,[new(new(100.62,67.64),"Tower 4"),new(new(104.49,63.71),"Tower 3"),new(new(100.37,59.23),"Tower 2"),new(new(95.8,62.82),"Tower 1"),new(new(118.75,70.93),"Valkyra"),new(new(138.03,67.33),"Weapons Vendor"),new(new(137.9,67.07),"Garage Vendor"),new(new(137.88,67.26),"Spawn Board"),new(new(68.28,88.03),"Manticore"),new(new(68.54,88.16),"Weapons Vendor"),new(new(68.66,87.92),"Garage Vendor"),new(new(68.42,87.86),"Spawn Board"),new(new(83.73,30.69),"Lonestar"),new(new(83.85,30.88),"Weapons Vendor"),new(new(83.73,31.17),"Garage Vendor"),new(new(83.68,31.04),"Spawn Board")],[[new(133.98,68.51),new(138.58,69.92),new(139.99,65.32),new(135.39,63.91)],[new(69.22,90.85),new(73.09,87.98),new(70.22,84.12),new(66.36,86.99)],[new(81.52,34.03),new(86.33,34.03),new(86.33,29.21),new(81.53,29.22)]]),
new("zestafona","Zestafona",-16322,-2,-0.03,163.81,-0.01,163.83,[new(new(70.172672,100.1717),"Tower 3"),new(new(68.599808,104.15299999999999),"Tower 1"),new(new(72.892416,105.07059199999999),"Tower 2"),new(new(39.436288,124.94438399999999),"VALKYRA Spawn"),new(new(104.66099200000001,115.081216),"MANTICORE Spawn"),new(new(68.009984,66.60096),"LONESTAR"),new(new(38.895616,124.862464),"Spawn Board"),new(new(104.66099200000001,114.54054400000001),"Garage Vendor"),new(new(105.054208,115.294208),"Spawn Board"),new(new(68.23935999999999,66.322432),"Spawn Board"),new(new(67.82976,66.502656),"Garage Vendor"),new(new(39.40352,124.79692800000001),"Garage Vendor")],[[new(103.3011,111.7061),new(101.9084,116.31),new(106.5123,117.68620000000001),new(107.88860000000001,113.09870000000001)],[new(40.2718,121.8805),new(35.700700000000005,123.35510000000001),new(37.175295999999996,127.92620000000001),new(41.7464,126.4517)],[new(65.1264,64.8151),new(66.306,69.4681),new(70.95909999999999,68.2721),new(69.7794,63.619)]]),
];
 public static readonly Weapon[] Weapons = [
new("L81 Mortar",132.0,684.0,[new(80,950),new(87,940),new(93,930),new(99,920),new(105,910),new(110,900),new(115,890),new(118,880),new(122,870),new(127,860),new(132,850),new(140,840),new(151,830),new(163,820),new(175,810),new(187,800),new(198,790),new(208,780),new(219,770),new(229,760),new(239,750),new(250,740),new(260,730),new(270,720),new(280,710),new(290,700),new(300,690),new(310,680),new(319,670),new(329,660),new(339,650),new(348,640),new(358,630),new(367,620),new(376,610),new(385,600),new(394,590),new(403,580),new(412,570),new(420,560),new(429,550),new(437,540),new(446,530),new(454,520),new(462,510),new(470,500),new(478,490),new(486,480),new(494,470),new(501,460),new(509,450),new(516,440),new(524,430),new(531,420),new(538,410),new(545,400),new(552,390),new(559,380),new(565,370),new(572,360),new(578,350),new(585,340),new(591,330),new(597,320),new(603,310),new(609,300),new(615,290),new(620,280),new(626,270),new(631,260),new(636,250),new(641,240),new(646,230),new(651,220),new(656,210),new(661,200),new(666,190),new(670,180),new(675,170),new(680,160),new(684,150),new(688,140),new(693,130),new(697,120)],[new(80,950),new(87,940),new(93,930),new(99,920),new(105,910),new(110,900),new(115,890),new(118,880),new(122,870),new(127,860),new(132,850),new(140,840),new(151,830),new(163,820),new(175,810),new(187,800),new(198,790),new(208,780),new(219,770),new(229,760),new(239,750),new(250,740),new(260,730),new(270,720),new(280,710),new(290,700),new(300,690),new(310,680),new(319,670),new(329,660),new(339,650),new(348,640),new(358,630),new(367,620),new(376,610),new(385,600),new(394,590),new(403,580),new(412,570),new(420,560),new(429,550),new(437,540),new(446,530),new(454,520),new(462,510),new(470,500),new(478,490),new(486,480),new(494,470),new(501,460),new(509,450),new(516,440),new(524,430),new(531,420),new(538,410),new(545,400),new(552,390),new(559,380),new(565,370),new(572,360),new(578,350),new(585,340),new(591,330),new(597,320),new(603,310),new(609,300),new(615,290),new(620,280),new(626,270),new(631,260),new(636,250),new(641,240),new(646,230),new(651,220),new(656,210),new(661,200),new(666,190),new(670,180),new(675,170),new(680,160),new(684,150),new(688,140),new(693,130),new(697,120)]),
new("SPH-2",780.0,2629.0,[new(1181,20),new(1232,30),new(1283,40),new(1334,50),new(1384,60),new(1433,70),new(1482,80),new(1529,90),new(1576,100),new(1622,110),new(1666,120),new(1709,130),new(1751,140),new(1792,150),new(1832,160),new(1870,170),new(1907,180),new(1944,190),new(1979,200),new(2014,210),new(2046,220),new(2079,230),new(2110,240),new(2139,250),new(2168,260),new(2196,270),new(2223,280),new(2249,290),new(2273,300),new(2296,310),new(2319,320),new(2341,330),new(2362,340),new(2383,350),new(2403,360),new(2422,370),new(2439,380),new(2456,390),new(2471,400),new(2485,410),new(2499,420),new(2513,430),new(2526,440),new(2538,450),new(2550,460),new(2561,470),new(2570,480),new(2579,490),new(2586,500),new(2593,510),new(2599,520),new(2605,530),new(2610,540),new(2615,550),new(2620,560),new(2623,570),new(2626,580),new(2628,590),new(2629,600)],[new(735,1400),new(780,1390),new(825,1380),new(869,1370),new(913,1360),new(956,1350),new(999,1340),new(1041,1330),new(1083,1320),new(1124,1310),new(1165,1300),new(1205,1290),new(1245,1280),new(1285,1270),new(1324,1260),new(1363,1250),new(1401,1240),new(1438,1230),new(1475,1220),new(1512,1210),new(1547,1200),new(1582,1190),new(1616,1180),new(1650,1170),new(1684,1160),new(1717,1150),new(1750,1140),new(1782,1130),new(1813,1120),new(1844,1110),new(1875,1100),new(1905,1090),new(1934,1080),new(1963,1070),new(1991,1060),new(2019,1050),new(2046,1040),new(2072,1030),new(2098,1020),new(2123,1010),new(2147,1000),new(2171,990),new(2194,980),new(2217,970),new(2239,960),new(2261,950),new(2282,940),new(2303,930),new(2323,920),new(2342,910),new(2360,900),new(2378,890),new(2395,880),new(2412,870),new(2429,860),new(2444,850),new(2460,840),new(2474,830),new(2488,820),new(2501,810),new(2513,800),new(2524,790),new(2536,780),new(2546,770),new(2557,760),new(2567,750),new(2576,740),new(2584,730),new(2592,720),new(2599,710),new(2604,700),new(2609,690),new(2613,680),new(2617,670),new(2621,660),new(2624,650),new(2626,640),new(2628,630),new(2629,610),new(2629,620)]),
 ];
 public static Point World(double x,double y,Map m)=>new((x/100-m.OffsetX)/100,-(y/100-m.OffsetY)/100);
 // Supplied wardogs-radar-byom/src/lib/maps.ts (DA_UI_* bounds).
 // Keep legacy artillery coordinates/saved points; only image projection changes.
 public static (double MinX,double MinY,double Size) ImageBounds(Map m)=>m.Id switch
 {
  "bakurani"=>(-1632000,-2040000,1632000),
  "ozeti"=>(-1616000,-1616000,1632000),
  "zestafona"=>(-819200,-819200,1638400),
  _=>throw new ArgumentOutOfRangeException(nameof(m))
 };
 public static Point WorldToImage(double x,double y,Map m)
 {var b=ImageBounds(m);return new((x-b.MinX)/b.Size,(y-b.MinY)/b.Size);}
 public static Point PointToImage(Point p,Map m)=>WorldToImage((p.X*100+m.OffsetX)*100,(m.OffsetY-p.Y*100)*100,m);
 public static Point ImageToPoint(double u,double v,Map m)
 {var b=ImageBounds(m);return World(b.MinX+u*b.Size,b.MinY+v*b.Size,m);}
 public static double? Interpolate(Point[] table,double distance)
 {
  if(!double.IsFinite(distance)||table.Length==0||distance<table[0].X||distance>table[^1].X)return null;
  for(int i=0;i<table.Length;i++) if(table[i].X>=distance)
  { if(i==0||table[i].X==distance)return table[i].Y;
    var a=table[i-1];var b=table[i];return a.Y+(b.Y-a.Y)*(distance-a.X)/(b.X-a.X); }
  return null;
 }
 public readonly record struct Solution(double Distance,double Bearing,double? Low,double? High);
 public static Solution Solve(Point gun,Point target,Weapon w)
 {
  double dx=target.X-gun.X,dy=target.Y-gun.Y,d=Math.Sqrt(dx*dx+dy*dy)*100;
  bool valid=d>=w.Min&&d<=w.Max;
  return new(d,(Math.Atan2(dx,dy)*180/Math.PI+360)%360,valid?Interpolate(w.Low,d):null,valid?Interpolate(w.High,d):null);
 }
}
public sealed record WardogArtillerySettings
{
 public bool AutoMap {get;init;}=true;
 public static bool HasDetectedMap(WardogReader.Frame frame) => frame.IsFresh && frame.DetectedMap is >=0 and <3
     && frame.MapSampleStarted > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(frame.MapSampleStarted).TotalMilliseconds is >=0 and <2000;
 public WardogArtillerySettings ApplyDetectedMap(WardogReader.Frame frame) => AutoMap && HasDetectedMap(frame) && Map != frame.DetectedMap
     ? this with {Map=frame.DetectedMap,Zoom=1,CenterX=.5f,CenterY=.5f} : this;
 public int Map {get;init;} public int Weapon {get;init;} public int Mode {get;init;}
 public bool Players {get;init;}=true; public bool Vehicles {get;init;}=true; public bool Locations {get;init;}=true;
 public float Zoom {get;init;}=1; public float CenterX {get;init;}=.5f; public float CenterY {get;init;}=.5f;
 public int PointMap {get;init;} public bool HasGun {get;init;} public bool HasTarget {get;init;}
 public float GunX {get;init;} public float GunY {get;init;} public float TargetX {get;init;} public float TargetY {get;init;}
 public bool Valid => Map is >=0 and <3 && Weapon is >=0 and <2 && Mode is >=0 and <=2 && PointMap is >=0 and <3
 && float.IsFinite(Zoom)&&Zoom is >=1 and <=128 && float.IsFinite(CenterX)&&CenterX is >=0 and <=1
 && float.IsFinite(CenterY)&&CenterY is >=0 and <=1 && float.IsFinite(GunX)&&Math.Abs(GunX)<=50000
 && float.IsFinite(GunY)&&Math.Abs(GunY)<=50000 && float.IsFinite(TargetX)&&Math.Abs(TargetX)<=50000
 && float.IsFinite(TargetY)&&Math.Abs(TargetY)<=50000;
}
