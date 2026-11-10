"""Focused captured-model pictures for one-action Reader steps."""
import html

WIDTH, HEIGHT = 960, 540

def picture_html(figure, frames):
    if "panels" in figure:
        panels = figure["panels"]
        if len(panels) != 2:
            raise ValueError("A comparison picture needs two equally sized panels")
        images = []
        for index, panel in enumerate(panels):
            frame = frames.pick(panel["shot"], panel["pick"])
            images.append(f'<img src="{html.escape(frames.path(panel["shot"],frame),quote=True)}" '
                          f'style="position:absolute;width:480px;height:270px;left:{index*480}px;top:135px">')
        return '<!doctype html><html><body style="margin:0;background:white">' + ''.join(images) + '</body></html>'
    shot = figure["shot"]
    frame = frames.pick(shot, figure["pick"])
    anchor = frame["anchors"].get(figure.get("anchor"))
    if figure.get("anchor") and (not anchor or not anchor[2]):
        raise ValueError("Step target is not visible: " + figure["anchor"])
    if anchor:
        crop_w, crop_h = 800, 450
        x = max(0, min(1280 - crop_w, anchor[0] - crop_w / 2))
        y = max(0, min(720 - crop_h, anchor[1] - crop_h / 2))
    else:
        x, y, crop_w, crop_h = 0, 0, 1280, 720
    scale = WIDTH / crop_w
    marker = ""
    if anchor:
        ax, ay = (anchor[0] - x) * scale, (anchor[1] - y) * scale
        marker = (f'<svg width="{WIDTH}" height="{HEIGHT}" style="position:absolute;left:0;top:0">'
                  f'<circle cx="{ax}" cy="{ay}" r="25" fill="none" stroke="white" stroke-width="10"/>'
                  f'<circle cx="{ax}" cy="{ay}" r="25" fill="none" stroke="#1F5FAF" stroke-width="5"/></svg>')
    return ('<!doctype html><html><body style="margin:0;overflow:hidden;background:white">'
            f'<img src="{html.escape(frames.path(shot,frame),quote=True)}" '
            f'style="position:absolute;width:{1280*scale}px;height:{720*scale}px;left:{-x*scale}px;top:{-y*scale}px">'
            + marker + '</body></html>')

def build(browser, content, frames, output):
    if content.get("step_layout") != "target_picture":
        return []
    pictures = []
    for index, step in enumerate(content["steps"], 1):
        path = output / f"step{index:02d}.png"
        browser.shot(picture_html(step["figure"], frames), path, size=(WIDTH, HEIGHT))
        pictures.append(path)
    return pictures
