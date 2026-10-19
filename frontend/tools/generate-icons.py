# Generates the agenda icons in apps/web/public/images/icons/ — filled
# single-colour silhouettes in the logo's navy, consumed as CSS masks by
# AppIcon (apps/web/src/app/app-icon.ts), so the file colour only matters
# when an SVG is opened directly.
#
# Usage (from frontend/): python3 tools/generate-icons.py apps/web/public/images/icons
import math, sys, os
out = sys.argv[1]
NAVY = '#092740'

def rect(x, y, w, h, r=0):
    return f'<rect x="{x:g}" y="{y:g}" width="{w:g}" height="{h:g}" rx="{r:g}"/>'
def circle(cx, cy, r):
    return f'<circle cx="{cx:g}" cy="{cy:g}" r="{r:g}"/>'
def path(d):
    return f'<path d="{d}"/>'
def poly(pts):
    return '<path d="M' + 'L'.join(f'{x:.2f} {y:.2f}' for x, y in pts) + 'Z"/>'
def star(cx, cy, ro, ri, n=5, rot=-90):
    pts = []
    for i in range(2 * n):
        r = ro if i % 2 == 0 else ri
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return poly(pts)
def gear(cx, cy, ro, ri, teeth=8):
    pts = []
    step = 360 / teeth
    for i in range(teeth):
        b = i * step
        for a, r in ((b - step * .22, ro), (b + step * .22, ro), (b + step * .32, ri), (b + step * .68, ri)):
            t = math.radians(a)
            pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return poly(pts)

# Each icon: list of ('fill'|'cut', [elements]). A cut removes from everything
# drawn before it, so later fills sit on top of earlier cuts.
ICONS = {
    'home': [
        ('fill', [path('M12 2.2 1.2 11.4h3.1V21.5h15.4V11.4h3.1Z')]),
        ('cut', [rect(9.6, 14, 4.8, 8, 1)]),
    ],
    'grades': [
        ('fill', [rect(3.5, 1.8, 17, 20.4, 2.6)]),
        ('cut', [path('M10.3 7.2 13.4 5.4h2.1v11.2h2.3v2.2H9.9v-2.2h2.7V8.6l-2.3 1.2Z')]),
    ],
    'absences': [
        ('fill', [circle(9, 6.8, 4.3), path('M1.2 21.5c0-4.9 3.5-8.4 7.8-8.4s7.8 3.5 7.8 8.4Z')]),
        ('cut', [circle(18, 16.5, 6.4)]),
        ('fill', [circle(18, 16.5, 5)]),
        ('cut', [poly([(15.2, 16.6), (16.4, 15.4), (17.4, 16.4), (19.7, 14.1), (20.9, 15.3), (17.4, 18.8)])]),
    ],
    'timetable': [
        ('fill', [rect(1.8, 2.8, 20.4, 18.4, 2.6)]),
        ('cut', [rect(3.8 + c * 5.65, 8.6 + r * 6.1, 4.75, 4.9, .8) for c in range(3) for r in range(2)]),
    ],
    'events': [
        ('fill', [rect(2.2, 4.2, 19.6, 17.4, 2.6)]),
        ('cut', [rect(5.2, 1, 4.6, 6.4, 2.3), rect(14.2, 1, 4.6, 6.4, 2.3), rect(2.2, 8.6, 19.6, 1.4)]),
        ('fill', [rect(6.5, 1.8, 2, 4.8, 1), rect(15.5, 1.8, 2, 4.8, 1)]),
        ('cut', [star(12, 15.6, 4.6, 1.9)]),
    ],
    'messages': [
        ('fill', [rect(1.8, 2.8, 20.4, 14.2, 3.2), path('M5.5 15.5V22l6.2-6.5Z')]),
        ('cut', [circle(7.4, 9.9, 1.55), circle(12, 9.9, 1.55), circle(16.6, 9.9, 1.55)]),
    ],
    'homework': [
        ('fill', [path('M1.5 5.2C4.6 3.3 8.4 3.3 11.2 5.4V21c-2.9-1.9-6.6-1.9-9.7-.2Z'),
                  path('M22.5 5.2C19.4 3.3 15.6 3.3 12.8 5.4V21c2.9-1.9 6.6-1.9 9.7-.2Z')]),
        ('cut', [path('M3.8 9.2c1.7-.7 3.6-.7 5.2 0v1.3c-1.6-.7-3.5-.7-5.2 0Z'),
                 path('M3.8 12.6c1.7-.7 3.6-.7 5.2 0v1.3c-1.6-.7-3.5-.7-5.2 0Z'),
                 path('M15 9.2c1.6-.7 3.5-.7 5.2 0v1.3c-1.7-.7-3.6-.7-5.2 0Z'),
                 path('M15 12.6c1.6-.7 3.5-.7 5.2 0v1.3c-1.7-.7-3.6-.7-5.2 0Z')]),
    ],
    'behavior': [
        ('fill', [poly([(5.2, 1.5), (10.2, 1.5), (13, 8.4), (8.6, 10.6)]),
                  poly([(18.8, 1.5), (13.8, 1.5), (11, 8.4), (15.4, 10.6)])]),
        ('cut', [circle(12, 15.2, 8.2)]),
        ('fill', [circle(12, 15.2, 6.9)]),
        ('cut', [star(12, 15.4, 4.4, 1.85)]),
    ],
    'payments': [
        ('fill', [path('M3.6 6.4 15.8 2.2c1-.35 2 .2 2.3 1.1L19 6.4Z'), rect(1.8, 6.4, 19.6, 15, 2.6)]),
        ('cut', [rect(14.6, 11, 9, 5.6, 2.2)]),
        ('fill', [rect(15.9, 12.2, 6.3, 3.2, 1.6)]),
        ('cut', [circle(17.9, 13.8, 1.05)]),
    ],
    'admin': [
        ('fill', [gear(12, 12, 10.6, 7.9, 8)]),
        ('cut', [circle(12, 12, 3.4)]),
    ],
    'school': [
        ('fill', [path('M12 1.6 1.4 7.6h21.2Z'), rect(2.6, 8.4, 18.8, 1.8, .4),
                  rect(4, 11.2, 2.6, 7.4, .3), rect(8.5, 11.2, 2.6, 7.4, .3),
                  rect(12.9, 11.2, 2.6, 7.4, .3), rect(17.4, 11.2, 2.6, 7.4, .3),
                  rect(1.4, 19.4, 21.2, 2.6, .5)]),
        ('cut', [circle(12, 5.4, 1.2)]),
    ],
    'logs': [
        ('fill', [rect(3.5, 3.2, 17, 19, 2.6)]),
        ('cut', [rect(7.4, 1, 9.2, 5.2, 2)]),
        ('fill', [rect(8.6, 1.8, 6.8, 3.6, 1.4)]),
        ('cut', [rect(7, 9.4, 10, 1.7, .85), rect(7, 13.2, 10, 1.7, .85), rect(7, 17, 6.2, 1.7, .85)]),
    ],
}

def render(layers):
    body = ''
    defs = ''
    for i, (kind, els) in enumerate(layers):
        if kind == 'fill':
            body += ''.join(els)
        else:
            mid = f'c{i}'
            defs += f'<mask id="{mid}" maskUnits="userSpaceOnUse" x="0" y="0" width="24" height="24"><rect width="24" height="24" fill="#fff"/><g fill="#000">{"".join(els)}</g></mask>'
            body = f'<g mask="url(#{mid})">{body}</g>'
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">'
            f'<defs>{defs}</defs><g fill="{NAVY}">{body}</g></svg>\n')

for name, layers in ICONS.items():
    with open(os.path.join(out, name + '.svg'), 'w') as f:
        f.write(render(layers))
print(len(ICONS))
