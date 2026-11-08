# Run inside RenderDoc: qrenderdoc.exe --python rdc_actions.py
# Reads RDC_IN (capture path) and RDC_OUT (text report path) from the environment and writes the
# action tree: event id, kind, name/marker path, dispatch size, and the outputs of each draw/dispatch.
import os
import sys
import renderdoc as rd

capture_path = os.environ['RDC_IN']
report_path = os.environ['RDC_OUT']
out = open(report_path, 'w', encoding='utf-8')


def log(text):
    out.write(text + '\n')
    out.flush()


rd.InitialiseReplay(rd.GlobalEnvironment(), [])
cap = rd.OpenCaptureFile()
status = cap.OpenFile(capture_path, '', None)
if status != rd.ResultCode.Succeeded:
    log('open failed: %s' % status)
    sys.exit(1)
status, controller = cap.OpenCapture(rd.ReplayOptions(), None)
if status != rd.ResultCode.Succeeded:
    log('replay failed: %s' % status)
    sys.exit(1)

names = {}
for res in controller.GetResources():
    names[int(res.resourceId)] = res.name
textures = {int(t.resourceId): t for t in controller.GetTextures()}


def tex_desc(rid):
    t = textures.get(int(rid))
    if t is None:
        return names.get(int(rid), str(rid))
    return '%s %dx%d %s' % (names.get(int(rid), str(rid)), t.width, t.height, t.format.Name())


counts = {'draw': 0, 'dispatch': 0, 'other': 0}


def walk(actions, path, depth):
    for a in actions:
        flags = a.flags
        kind = 'draw' if flags & rd.ActionFlags.Drawcall else 'dispatch' if flags & rd.ActionFlags.Dispatch else 'marker' if a.children else 'other'
        name = a.GetName(controller.GetStructuredFile())
        line = '%6d %-8s %s%s' % (a.eventId, kind, '  ' * depth, name)
        if kind == 'dispatch':
            line += '  groups=%s' % (tuple(a.dispatchDimension),)
            counts['dispatch'] += 1
        elif kind == 'draw':
            line += '  idx=%d inst=%d' % (a.numIndices, a.numInstances)
            counts['draw'] += 1
        else:
            counts['other'] += 1
        outs = [o for o in a.outputs if o != rd.ResourceId.Null()]
        if outs:
            line += '  -> ' + ', '.join(tex_desc(o) for o in outs)
        if a.depthOut != rd.ResourceId.Null():
            line += '  depth ' + tex_desc(a.depthOut)
        log(line)
        if a.children:
            walk(a.children, path + [name], depth + 1)


walk(controller.GetRootActions(), [], 0)
log('# totals: %s' % counts)
controller.Shutdown()
cap.Shutdown()
rd.ShutdownReplay()
out.close()
os._exit(0)
