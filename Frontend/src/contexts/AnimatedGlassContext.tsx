import React, { createContext, useContext, ReactNode } from 'react';

interface AnimatedGlassContextType {
  animatedGlass: boolean;
  setAnimatedGlass: (enabled: boolean) => Promise<void>;
}

const AnimatedGlassContext = createContext<AnimatedGlassContextType | undefined>(undefined);

export const AnimatedGlassProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  // Transparency is always enabled (blur removed globally)
  const animatedGlass = true;

  const setAnimatedGlass = async (_enabled: boolean) => {
    // No-op: transparency is always on
  };

  return (
    <AnimatedGlassContext.Provider value={{ animatedGlass, setAnimatedGlass }}>
      {children}
    </AnimatedGlassContext.Provider>
  );
};

export const useAnimatedGlass = (): AnimatedGlassContextType => {
  const context = useContext(AnimatedGlassContext);
  if (!context) {
    throw new Error('useAnimatedGlass must be used within AnimatedGlassProvider');
  }
  return context;
};
