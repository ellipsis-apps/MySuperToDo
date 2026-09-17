// Small helper used by MainLayout to detect initial screen size
window.layoutHelpers = {
    isLargeScreen: function () {
        try {
            return window.matchMedia('(min-width: 769px)').matches;
        }
        catch {
            return true;
        }
    }
};
