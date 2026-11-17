fn main() {
    let mut res = winres::WindowsResource::new();
    res.set_icon("../WallpaperScheduler.Package/Assets/AppIcon.ico");
    let _ = res.compile();
}
