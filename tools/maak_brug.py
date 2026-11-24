# Tekent een eigen LCARS-scheepsbrug als SVG (2400x900), alleen themakleuren.
# Geen filters of tekst: WinUI's SvgImageSource (Direct2D) ondersteunt die niet goed.
import math, random, sys

W, H = 2400, 900
ORANJE, DONKERORANJE, PERZIK = "#FF9900", "#CC7A00", "#FFCC99"
LILA, ROZE, PAARS = "#9999CC", "#CC6666", "#7774F5"
rnd = random.Random(1701)
out = []
a = out.append

def bilin(quad, u, v):
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = quad  # lb, rb, ro, lo
    tx, ty = x0 + (x1 - x0) * u, y0 + (y1 - y0) * u
    bx, by = x3 + (x2 - x3) * u, y3 + (y2 - y3) * u
    return tx + (bx - tx) * v, ty + (by - ty) * v

def ronde_rect_punten(u0, v0, u1, v1, r, n=8, hoeken=(1, 1, 1, 1)):
    # r in u-eenheden; v-straal geschaald zodat het er rond uitziet
    pts = []
    ru, rv = r, r * 3.2
    ru = min(ru, (u1 - u0) / 2); rv = min(rv, (v1 - v0) / 2)
    centra = [(u1 - ru, v0 + rv, -90, 0), (u1 - ru, v1 - rv, 0, 90),
              (u0 + ru, v1 - rv, 90, 180), (u0 + ru, v0 + rv, 180, 270)]
    hoekpunt = [(u1, v0), (u1, v1), (u0, v1), (u0, v0)]
    for i, (cu, cv, s, e) in enumerate(centra):
        if hoeken[i]:
            for k in range(n + 1):
                t = math.radians(s + (e - s) * k / n)
                pts.append((cu + ru * math.cos(t), cv + rv * math.sin(t)))
        else:
            pts.append(hoekpunt[i])
    return pts

def poly(quad, pts, fill, extra=""):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in (bilin(quad, u, v) for u, v in pts))
    a(f'<polygon points="{p}" fill="{fill}" {extra}/>')

a(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">')
a('''<defs>
<linearGradient id="lucht" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#020210"/><stop offset="1" stop-color="#0B0A26"/></linearGradient>
<radialGradient id="nevel1" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#B63C50" stop-opacity="0.55"/><stop offset="1" stop-color="#B63C50" stop-opacity="0"/></radialGradient>
<radialGradient id="nevel2" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#7774F5" stop-opacity="0.45"/><stop offset="1" stop-color="#7774F5" stop-opacity="0"/></radialGradient>
<radialGradient id="planeet" cx="0.32" cy="0.3" r="0.8"><stop offset="0" stop-color="#FFE2C2"/><stop offset="0.25" stop-color="#FFB16C"/><stop offset="0.55" stop-color="#FF8066"/><stop offset="0.8" stop-color="#5A1E2A"/><stop offset="1" stop-color="#0A0410"/></radialGradient>
<radialGradient id="maan" cx="0.3" cy="0.3" r="0.8"><stop offset="0" stop-color="#E6E6FF"/><stop offset="0.6" stop-color="#9999CC"/><stop offset="1" stop-color="#1A1A33"/></radialGradient>
<radialGradient id="schermgloed" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FF9900" stop-opacity="0.16"/><stop offset="1" stop-color="#FF9900" stop-opacity="0"/></radialGradient>
<linearGradient id="plafond" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#15142C"/><stop offset="1" stop-color="#05050C"/></linearGradient>
<linearGradient id="wandL" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#1C1A38"/><stop offset="1" stop-color="#0A0A18"/></linearGradient>
<linearGradient id="wandR" x1="1" y1="0" x2="0" y2="0"><stop offset="0" stop-color="#1C1A38"/><stop offset="1" stop-color="#0A0A18"/></linearGradient>
<linearGradient id="vloer" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0E0C1E"/><stop offset="1" stop-color="#241A30"/></linearGradient>
<linearGradient id="reling" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFCC99"/><stop offset="0.35" stop-color="#FF9900"/><stop offset="1" stop-color="#7A3A10"/></linearGradient>
<linearGradient id="console" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2A2448"/><stop offset="1" stop-color="#0C0A18"/></linearGradient>
<linearGradient id="stoel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3A2A3E"/><stop offset="1" stop-color="#140E1A"/></linearGradient>
<linearGradient id="kopbalk" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#FFB16C"/><stop offset="0.5" stop-color="#FF8066"/><stop offset="1" stop-color="#B63C50"/></linearGradient>
<linearGradient id="zijbalk" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#8E89FF"/><stop offset="1" stop-color="#7774F5"/></linearGradient>
<radialGradient id="lichtvlek" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#FFCC99" stop-opacity="0.22"/><stop offset="1" stop-color="#FFCC99" stop-opacity="0"/></radialGradient>
<clipPath id="scherm"><rect x="720" y="120" width="960" height="360" rx="28"/></clipPath>
</defs>''')

# Achtergrond, plafond en wanden
a(f'<rect width="{W}" height="{H}" fill="#000"/>')
a('<path d="M0,0 H2400 V150 Q1200,40 0,150 Z" fill="url(#plafond)"/>')
for i, y in enumerate((70, 98)):
    a(f'<path d="M80,{y+70} Q1200,{y-55} 2320,{y+70}" fill="none" stroke="{LILA if i else PERZIK}" stroke-opacity="{0.35 if i else 0.5}" stroke-width="{4 if i else 6}" stroke-linecap="round"/>')
# plafondlampjes
for k in range(13):
    t = k / 12
    t = 0.06 + t * 0.88
    x = 80 + t * 2240
    y = 154 - 250 * t * (1 - t)
    a(f'<ellipse cx="{x:.0f}" cy="{y:.0f}" rx="22" ry="5" fill="{PERZIK}" fill-opacity="0.55"/>')

wandL = [(0, 150), (700, 140), (700, 500), (0, 660)]
wandR = [(1700, 140), (2400, 150), (2400, 660), (1700, 500)]
a('<polygon points="0,150 700,140 700,500 0,660" fill="url(#wandL)"/>')
a('<polygon points="1700,140 2400,150 2400,660 1700,500" fill="url(#wandR)"/>')

# LCARS-panelen op de wanden (in perspectief)
def wandpanelen(quad, spiegel):
    def U(u): return 1 - u if spiegel else u
    def blok(u0, v0, u1, v1, r, kleur, hoeken=(1, 1, 1, 1), extra=""):
        if spiegel:
            u0, u1 = 1 - u1, 1 - u0
            hoeken = (hoeken[3], hoeken[2], hoeken[1], hoeken[0])
        poly(quad, ronde_rect_punten(u0, v0, u1, v1, r, hoeken=hoeken), kleur, extra)
    # grote elleboog-frame
    blok(0.06, 0.12, 0.17, 0.80, 0.05, ORANJE, hoeken=(0, 0, 1, 1))
    blok(0.06, 0.12, 0.62, 0.22, 0.04, ORANJE, hoeken=(1, 0, 0, 1))
    blok(0.64, 0.12, 0.80, 0.22, 0.04, ROZE)
    blok(0.82, 0.12, 0.92, 0.22, 0.04, PERZIK)
    blok(0.06, 0.83, 0.17, 0.90, 0.04, LILA)
    # beeldschermpje met staafjes
    blok(0.21, 0.30, 0.90, 0.78, 0.015, "#06061A")
    kleuren = [ORANJE, LILA, PERZIK, ROZE, PAARS]
    for r in range(6):
        v = 0.34 + r * 0.073
        u = 0.24
        while u < 0.86:
            lengte = rnd.uniform(0.04, 0.16)
            u1 = min(u + lengte, 0.87)
            blok(u, v, u1, v + 0.045, 0.012, rnd.choice(kleuren), extra=f'fill-opacity="{rnd.uniform(0.55, 1):.2f}"')
            u = u1 + 0.015
    blok(0.21, 0.84, 0.55, 0.90, 0.02, "url(#zijbalk)")
    blok(0.57, 0.84, 0.90, 0.90, 0.02, DONKERORANJE)

wandpanelen(wandL, False)
wandpanelen(wandR, True)

# Kijkscherm
a('<ellipse cx="1200" cy="320" rx="900" ry="380" fill="url(#schermgloed)"/>')
a('<rect x="700" y="100" width="1000" height="400" rx="44" fill="#14122A"/>')
a('<g clip-path="url(#scherm)">')
a('<rect x="720" y="120" width="960" height="360" fill="url(#lucht)"/>')
a('<ellipse cx="930" cy="230" rx="330" ry="150" fill="url(#nevel1)"/>')
a('<ellipse cx="1100" cy="330" rx="380" ry="130" fill="url(#nevel2)"/>')
a('<ellipse cx="1560" cy="190" rx="220" ry="110" fill="url(#nevel2)"/>')
for _ in range(170):
    x, y = rnd.uniform(720, 1680), rnd.uniform(120, 480)
    r = rnd.choice((0.8, 1, 1.2, 1.5, 2, 2.6))
    kleur = rnd.choice(("#FFFFFF", "#FFFFFF", "#FFFFFF", PERZIK, "#CCCCFF"))
    a(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{kleur}" fill-opacity="{rnd.uniform(0.45, 1):.2f}"/>')
# warp-strepen vanaf het midden, heel subtiel
for _ in range(26):
    hoek = rnd.uniform(0, 2 * math.pi)
    d0, d1 = rnd.uniform(60, 160), rnd.uniform(220, 520)
    cx, cy = 1200, 300
    a(f'<line x1="{cx + math.cos(hoek) * d0:.0f}" y1="{cy + math.sin(hoek) * d0 * 0.6:.0f}" x2="{cx + math.cos(hoek) * d1:.0f}" y2="{cy + math.sin(hoek) * d1 * 0.6:.0f}" stroke="#CCCCFF" stroke-opacity="0.18" stroke-width="1.4"/>')
a('<circle cx="1470" cy="470" r="230" fill="url(#planeet)"/>')
a('<circle cx="1470" cy="470" r="232" fill="none" stroke="#FFCC99" stroke-opacity="0.35" stroke-width="3"/>')
a('<path d="M1225,505 Q1470,420 1715,505" fill="none" stroke="#FFCC99" stroke-opacity="0.18" stroke-width="10"/>')
a('<circle cx="1150" cy="200" r="34" fill="url(#maan)"/>')
a('</g>')
a('<rect x="720" y="120" width="960" height="360" rx="28" fill="none" stroke="#9999CC" stroke-opacity="0.5" stroke-width="3"/>')
# LCARS-lijst boven en onder het scherm
a('<path d="M700,92 H1040 V108 H700 Z" fill="none"/>')
a(f'<rect x="760" y="74" width="300" height="18" rx="9" fill="url(#kopbalk)"/>')
a(f'<rect x="1072" y="74" width="120" height="18" rx="9" fill="{LILA}"/>')
a(f'<rect x="1204" y="74" width="436" height="18" rx="9" fill="{ORANJE}"/>')
a(f'<rect x="760" y="508" width="200" height="16" rx="8" fill="{ROZE}"/>')
a(f'<rect x="972" y="508" width="456" height="16" rx="8" fill="url(#zijbalk)"/>')
a(f'<rect x="1440" y="508" width="200" height="16" rx="8" fill="{PERZIK}"/>')

# Vloer
a('<path d="M0,660 L700,500 H1700 L2400,660 V900 H0 Z" fill="url(#vloer)"/>')
a('<ellipse cx="1200" cy="760" rx="760" ry="120" fill="url(#lichtvlek)"/>')
# vloerlijnen naar het verdwijnpunt
for k in range(-6, 7):
    x0 = 1200 + k * 230
    a(f'<line x1="{1200 + k * 75}" y1="500" x2="{x0}" y2="900" stroke="#9999CC" stroke-opacity="0.07" stroke-width="2"/>')

# Consoles (stuur en ops) voor het scherm
def console(cx, kleur):
    a(f'<path d="M{cx-150},560 L{cx+150},560 L{cx+175},630 L{cx-175},630 Z" fill="url(#console)"/>')
    a(f'<path d="M{cx-150},560 L{cx+150},560 L{cx+140},548 L{cx-140},548 Z" fill="#3A3366"/>')
    for i in range(7):
        x = cx - 128 + i * 37
        k = (ORANJE, LILA, PERZIK, ROZE, ORANJE, PAARS, LILA)[(i + (1 if kleur else 0)) % 7]
        a(f'<rect x="{x}" y="552" width="30" height="6" rx="3" fill="{k}" fill-opacity="0.9"/>')
    a(f'<rect x="{cx-175}" y="628" width="350" height="8" rx="4" fill="{DONKERORANJE}" fill-opacity="0.7"/>')
    a(f'<ellipse cx="{cx}" cy="640" rx="190" ry="10" fill="#000" fill-opacity="0.5"/>')
console(1000, False)
console(1400, True)

# Gebogen reling achter de commandostoelen
a('<path d="M120,800 Q1200,630 2280,800 L2280,826 Q1200,662 120,826 Z" fill="url(#reling)"/>')
a('<path d="M120,826 Q1200,662 2280,826 L2280,900 L120,900 Z" fill="#100C18"/>')

# Commandostoelen (drie, midden groter)
def stoel(cx, basis, s):
    w, h = 150 * s, 190 * s
    top = basis - h
    a(f'<ellipse cx="{cx}" cy="{basis+6*s:.0f}" rx="{w*0.75:.0f}" ry="{14*s:.0f}" fill="#000" fill-opacity="0.6"/>')
    # rugleuning
    a(f'<rect x="{cx-w*0.38:.0f}" y="{top:.0f}" width="{w*0.76:.0f}" height="{h*0.78:.0f}" rx="{26*s:.0f}" fill="url(#stoel)" stroke="{ORANJE}" stroke-opacity="0.45" stroke-width="{3*s:.1f}"/>')
    # armleuningen met lampjes
    for kant in (-1, 1):
        x = cx + kant * w * 0.38 - (w * 0.2 if kant < 0 else 0)
        a(f'<rect x="{x:.0f}" y="{top + h*0.5:.0f}" width="{w*0.2:.0f}" height="{h*0.42:.0f}" rx="{10*s:.0f}" fill="#241A2C" stroke="{LILA}" stroke-opacity="0.4" stroke-width="{2*s:.1f}"/>')
        a(f'<rect x="{x + w*0.03:.0f}" y="{top + h*0.53:.0f}" width="{w*0.14:.0f}" height="{6*s:.0f}" rx="{3*s:.0f}" fill="{ORANJE if kant<0 else LILA}"/>')
    # zitting
    a(f'<rect x="{cx-w*0.36:.0f}" y="{top + h*0.72:.0f}" width="{w*0.72:.0f}" height="{h*0.2:.0f}" rx="{12*s:.0f}" fill="#2C2034"/>')
    a(f'<rect x="{cx-w*0.1:.0f}" y="{top + h*0.92:.0f}" width="{w*0.2:.0f}" height="{h*0.08:.0f}" fill="#120C16"/>')

stoel(960, 860, 0.85)
stoel(1440, 860, 0.85)
stoel(1200, 880, 1.15)

# LCARS-rand onderaan, zoals de balken van de app
a(f'<rect x="0" y="884" width="560" height="16" fill="{ORANJE}"/>')
a(f'<rect x="572" y="884" width="180" height="16" fill="{LILA}"/>')
a(f'<rect x="1648" y="884" width="180" height="16" fill="{ROZE}"/>')
a(f'<rect x="1840" y="884" width="560" height="16" fill="{ORANJE}"/>')
a('</svg>')

with open(sys.argv[1], "w", encoding="utf-8") as f:
    f.write("\n".join(out))
