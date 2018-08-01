import React from 'react';
import { StackNavigator } from 'react-navigation';
import {
  StyleSheet, Text, View, Button,
} from 'react-native';
import SignIn from './SignIn';
import SignUp from './SignUp';


const RutasNoAutenticadas = StackNavigator(
  {
    SignIn: {
      screen: SignIn,
    },
    SignUp: {
      screen: SignUp,
    },
  },
  {
    headerMode: 'none',
  },
);

const styles = StyleSheet.create({
  containerComponent: {
    flex: 1,
    // justifyContent: 'center',
  },
});


export { RutasNoAutenticadas };
