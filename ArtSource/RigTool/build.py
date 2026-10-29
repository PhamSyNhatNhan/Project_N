"""Builds rig_tool.html from rig_tool.template.html, embedding the base humanoid content (JSON + PNGs) so the tool opens offline.

    python build.py [content folder]      default: ../../Assets/StreamingAssets/BaseContent/visual
"""
import base64, json, os, sys
h = os.path.dirname(os.path.abspath(__file__))
content = sys.argv[1] if len(sys.argv) > 1 else os.path.join(h, '..', '..', 'Assets', 'StreamingAssets', 'BaseContent', 'visual')
doc = json.load(open(os.path.join(content, 'humanoid.json'), encoding='utf-8'))
imgs = {}
for part in doc['extensions']['project_n']['visualParts']:
    path = part['image']
    imgs[path] = 'data:image/png;base64,' + base64.b64encode(open(os.path.join(content, '..', path.replace('/', os.sep)), 'rb').read()).decode()
t = open(os.path.join(h, 'rig_tool.template.html'), encoding='utf-8').read()
t = t.replace('/*DOC*/', json.dumps(doc)).replace('/*IMGS*/', json.dumps(imgs))
open(os.path.join(h, 'rig_tool.html'), 'w', encoding='utf-8').write(t)
print('rig_tool.html', len(t), 'bytes,', len(imgs), 'images')
