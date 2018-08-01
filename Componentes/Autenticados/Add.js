import React, { Component } from 'react';
import { View, Text } from 'react-native';

export default class Add extends Component {
  render() {
    return (
      <View style={styles.container}>
        <Text> AddComponent </Text>
      </View>
    );
  }
}

const styles = ({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#2c3e50',
  },
});
