import tailwindcss from 'tailwindcss';
import autoprefixer from 'autoprefixer';

// Sciter's CSS engine does not implement CSS Cascade Layers (@layer).
// This PostCSS plugin unwraps @layer blocks so the rules remain in source
// order without the @layer wrapper that Sciter rejects with
// "unrecognized css selector".
const sciterLayerPolyfill = {
  postcssPlugin: 'postcss-sciter-layer-polyfill',
  AtRule: {
    layer(rule) {
      if (rule.nodes?.length > 0) {
        rule.replaceWith(...rule.nodes);
      } else {
        rule.remove();
      }
    },
  },
};

export default {
  plugins: [
    tailwindcss,
    autoprefixer,
    sciterLayerPolyfill,
  ],
};
